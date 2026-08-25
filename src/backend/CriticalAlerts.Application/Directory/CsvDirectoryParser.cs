using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Simulation;

namespace CriticalAlerts.Application.Directory;

public sealed class CsvDirectorySourceAdapter : IDirectorySourceAdapter
{
    public string SourceSystem => DirectorySourceSystems.Csv;

    public DirectoryParseResult Read(Stream source) => CsvDirectoryParser.Parse(source, SourceSystem);
}

public static class CsvDirectoryParser
{
    private static readonly Regex SyntheticPhonePattern = new(
        @"\A(?:\+?1[\s.-]?)?555[\s.-]\d{3}[\s.-]\d{4}\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static readonly IReadOnlyList<string> RequiredHeaders =
    [
        "source_record_id",
        "first_name",
        "last_name",
        "simulation_code",
        "specialty",
        "site_code",
        "department_code",
        "role_title",
        "is_primary_role",
        "is_active",
        "source_updated_at_utc",
        "freshness_status",
    ];

    public static DirectoryParseResult Parse(string csvText, string sourceSystem = DirectorySourceSystems.Csv)
    {
        using var reader = new StringReader(csvText);
        return Parse(reader, sourceSystem);
    }

    public static DirectoryParseResult Parse(Stream source, string sourceSystem = DirectorySourceSystems.Csv)
    {
        using var reader = new StreamReader(source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return Parse(reader, sourceSystem);
    }

    public static DirectoryParseResult Parse(TextReader reader, string sourceSystem = DirectorySourceSystems.Csv)
    {
        var errors = new List<DirectoryImportIssue>();
        var warnings = new List<DirectoryImportIssue>();
        var rows = ReadRows(reader, errors);
        if (rows.Count == 0)
        {
            errors.Add(Issue("empty-csv", string.Empty, null, "The CSV is empty."));
            return new DirectoryParseResult(sourceSystem, [], errors, warnings);
        }

        if (rows[0].Values is null)
        {
            return new DirectoryParseResult(sourceSystem, [], errors, warnings);
        }

        var headerValues = rows[0].Values!;
        var header = headerValues.Select(value => value.Trim().ToLowerInvariant()).ToArray();
        var headerIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < header.Length; index++)
        {
            if (header[index].Length == 0)
            {
                continue;
            }

            if (!headerIndex.TryAdd(header[index], index))
            {
                errors.Add(Issue("duplicate-header", string.Empty, rows[0].RowNumber, $"CSV header '{header[index]}' appears more than once."));
            }
        }

        foreach (var required in RequiredHeaders)
        {
            if (!headerIndex.ContainsKey(required))
            {
                errors.Add(Issue("missing-header", string.Empty, 1, $"CSV header '{required}' is required."));
            }
        }

        if (errors.Count > 0)
        {
            return new DirectoryParseResult(sourceSystem, [], errors, warnings);
        }

        var grouped = new Dictionary<string, List<(int RowNumber, CsvRow Row)>>(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index < rows.Count; index++)
        {
            var row = rows[index];
            if (row.Values is null)
            {
                continue;
            }

            var rowNumber = row.RowNumber;
            if (row.Values.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (row.Values.Count != header.Length)
            {
                errors.Add(Issue(
                    "invalid-column-count",
                    string.Empty,
                    rowNumber,
                    $"CSV row must contain exactly {header.Length} columns."));
                continue;
            }

            var raw = new CsvRow(headerIndex, row.Values);
            var sourceRecordId = raw.Get("source_record_id");
            if (string.IsNullOrWhiteSpace(sourceRecordId))
            {
                errors.Add(Issue("missing-source-record-id", string.Empty, rowNumber, "source_record_id is required."));
                continue;
            }

            if (!grouped.TryGetValue(sourceRecordId, out var bucket))
            {
                bucket = [];
                grouped[sourceRecordId] = bucket;
            }

            bucket.Add((rowNumber, raw));
        }

        var practitioners = new List<NormalizedDirectoryPractitioner>();
        foreach (var (sourceRecordId, bucket) in grouped.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var parsed = ParseGroup(sourceRecordId, bucket, errors);
            if (parsed is not null)
            {
                practitioners.Add(parsed);
            }
        }

        var duplicateCodes = practitioners
            .GroupBy(practitioner => practitioner.SimulationCode, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1);
        foreach (var group in duplicateCodes)
        {
            foreach (var practitioner in group)
            {
                errors.Add(Issue(
                    "duplicate-simulation-code",
                    practitioner.SourceRecordId,
                    null,
                    $"simulation_code '{practitioner.SimulationCode}' is used by more than one source_record_id. Practitioners are never matched by name."));
            }
        }

        if (errors.Count > 0)
        {
            return new DirectoryParseResult(sourceSystem, [], errors, warnings);
        }

        return new DirectoryParseResult(sourceSystem, practitioners, errors, warnings);
    }

    private static NormalizedDirectoryPractitioner? ParseGroup(
        string sourceRecordId,
        List<(int RowNumber, CsvRow Row)> bucket,
        List<DirectoryImportIssue> errors)
    {
        var first = bucket[0];
        var identityErrors = 0;
        RequirePrefix(sourceRecordId, "source_record_id", first.RowNumber, errors, ref identityErrors);

        var firstName = RequireValue(first.Row, "first_name", sourceRecordId, first.RowNumber, errors, ref identityErrors);
        var lastName = RequireValue(first.Row, "last_name", sourceRecordId, first.RowNumber, errors, ref identityErrors);
        var simulationCode = RequireValue(first.Row, "simulation_code", sourceRecordId, first.RowNumber, errors, ref identityErrors);
        var specialty = RequireValue(first.Row, "specialty", sourceRecordId, first.RowNumber, errors, ref identityErrors);
        var isActive = RequireBoolean(first.Row, "is_active", sourceRecordId, first.RowNumber, errors, ref identityErrors);
        var sourceUpdated = RequireTimestamp(first.Row, "source_updated_at_utc", sourceRecordId, first.RowNumber, errors, ref identityErrors);
        var isStale = RequireFreshness(first.Row, sourceRecordId, first.RowNumber, errors, ref identityErrors);
        if (simulationCode.Length > 0)
        {
            RequirePrefix(simulationCode, "simulation_code", first.RowNumber, errors, ref identityErrors);
        }

        foreach (var (rowNumber, row) in bucket.Skip(1))
        {
            if (!Same(first.Row, row, "first_name")
                || !Same(first.Row, row, "last_name")
                || !Same(first.Row, row, "simulation_code")
                || !Same(first.Row, row, "specialty")
                || !Same(first.Row, row, "is_active")
                || !Same(first.Row, row, "source_updated_at_utc")
                || !Same(first.Row, row, "freshness_status"))
            {
                errors.Add(Issue(
                    "source-record-inconsistent",
                    sourceRecordId,
                    rowNumber,
                    "Rows that share source_record_id must have the same identity, activity, timestamp, and freshness values."));
                identityErrors++;
            }
        }

        var roles = new List<NormalizedDirectoryRole>();
        var endpoints = new List<NormalizedDirectoryEndpoint>();
        var onCall = new List<NormalizedDirectoryOnCall>();
        foreach (var (rowNumber, row) in bucket)
        {
            var siteCode = RequireValue(row, "site_code", sourceRecordId, rowNumber, errors, ref identityErrors);
            var departmentCode = RequireValue(row, "department_code", sourceRecordId, rowNumber, errors, ref identityErrors);
            var title = RequireValue(row, "role_title", sourceRecordId, rowNumber, errors, ref identityErrors);
            var isPrimary = RequireBoolean(row, "is_primary_role", sourceRecordId, rowNumber, errors, ref identityErrors);
            if (siteCode.Length > 0)
            {
                RequirePrefix(siteCode, "site_code", rowNumber, errors, ref identityErrors);
            }

            if (departmentCode.Length > 0)
            {
                RequirePrefix(departmentCode, "department_code", rowNumber, errors, ref identityErrors);
            }

            if (siteCode.Length > 0 && departmentCode.Length > 0 && title.Length > 0)
            {
                var role = new NormalizedDirectoryRole(siteCode, departmentCode, title, isPrimary);
                if (!roles.Contains(role))
                {
                    roles.Add(role);
                }
            }

            ParseEndpoint(row, sourceRecordId, rowNumber, endpoints, errors, ref identityErrors);
            ParseOnCall(row, sourceRecordId, rowNumber, siteCode, departmentCode, onCall, errors, ref identityErrors);
        }

        if (roles.Count(role => role.IsPrimary) > 1)
        {
            errors.Add(Issue("multiple-primary-roles", sourceRecordId, first.RowNumber, "A practitioner may have at most one primary role."));
            identityErrors++;
        }

        if (identityErrors > 0)
        {
            return null;
        }

        return new NormalizedDirectoryPractitioner(
            sourceRecordId,
            firstName,
            lastName,
            simulationCode,
            specialty,
            isActive,
            sourceUpdated,
            isStale,
            ComputeHash(sourceRecordId, firstName, lastName, simulationCode, specialty, isActive, sourceUpdated, isStale, roles, endpoints, onCall),
            roles,
            endpoints,
            onCall);
    }

    private static void ParseEndpoint(
        CsvRow row,
        string sourceRecordId,
        int rowNumber,
        List<NormalizedDirectoryEndpoint> endpoints,
        List<DirectoryImportIssue> errors,
        ref int identityErrors)
    {
        var kindText = row.Get("endpoint_kind");
        var value = row.Get("endpoint_value");
        var label = row.Get("endpoint_label");
        if (string.IsNullOrWhiteSpace(kindText) && string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(label))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(kindText) || string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(label))
        {
            errors.Add(Issue("incomplete-endpoint", sourceRecordId, rowNumber, "endpoint_kind, endpoint_value, and endpoint_label must be provided together."));
            identityErrors++;
            return;
        }

        if (!Enum.TryParse<ContactEndpointKind>(kindText, ignoreCase: true, out var kind))
        {
            errors.Add(Issue("invalid-endpoint-kind", sourceRecordId, rowNumber, "endpoint_kind must be Sms, Voice, or SecureMessage."));
            identityErrors++;
            return;
        }

        if (kind is ContactEndpointKind.Sms or ContactEndpointKind.Voice && !SyntheticPhonePattern.IsMatch(value.Trim()))
        {
            errors.Add(Issue(
                "non-synthetic-endpoint",
                sourceRecordId,
                rowNumber,
                "Simulation SMS and voice endpoints must use fictional 555 values."));
            identityErrors++;
            return;
        }

        if (kind == ContactEndpointKind.SecureMessage
            && !value.Trim().StartsWith("sim-secure://", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(Issue(
                "non-synthetic-endpoint",
                sourceRecordId,
                rowNumber,
                "Simulation secure-message endpoints must use the sim-secure:// scheme."));
            identityErrors++;
            return;
        }

        if (!SimulationEnvironmentPolicy.HasSyntheticPrefix(label))
        {
            errors.Add(Issue("invalid-endpoint-label", sourceRecordId, rowNumber, "endpoint_label values require a synthetic SIM- prefix."));
            identityErrors++;
            return;
        }

        var endpoint = new NormalizedDirectoryEndpoint(kind, value.Trim(), label.Trim());
        if (!endpoints.Contains(endpoint))
        {
            endpoints.Add(endpoint);
        }
    }

    private static void ParseOnCall(
        CsvRow row,
        string sourceRecordId,
        int rowNumber,
        string siteCode,
        string departmentCode,
        List<NormalizedDirectoryOnCall> onCall,
        List<DirectoryImportIssue> errors,
        ref int identityErrors)
    {
        var tierText = row.Get("on_call_tier");
        var starts = row.Get("on_call_starts_at_utc");
        var ends = row.Get("on_call_ends_at_utc");
        if (string.IsNullOrWhiteSpace(tierText) && string.IsNullOrWhiteSpace(starts) && string.IsNullOrWhiteSpace(ends))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(tierText) || string.IsNullOrWhiteSpace(starts) || string.IsNullOrWhiteSpace(ends))
        {
            errors.Add(Issue("incomplete-on-call", sourceRecordId, rowNumber, "on_call_tier, on_call_starts_at_utc, and on_call_ends_at_utc must be provided together."));
            identityErrors++;
            return;
        }

        if (!Enum.TryParse<OnCallTier>(tierText, ignoreCase: true, out var tier))
        {
            errors.Add(Issue("invalid-on-call-tier", sourceRecordId, rowNumber, "on_call_tier must be Primary or Backup."));
            identityErrors++;
            return;
        }

        if (!TryParseTimestamp(starts, out var startsAt) || !TryParseTimestamp(ends, out var endsAt))
        {
            errors.Add(Issue("invalid-on-call-timestamp", sourceRecordId, rowNumber, "On-call timestamps must be UTC instants."));
            identityErrors++;
            return;
        }

        var assignment = new NormalizedDirectoryOnCall(siteCode, departmentCode, tier, startsAt, endsAt);
        if (!onCall.Contains(assignment))
        {
            onCall.Add(assignment);
        }
    }

    private static string RequireValue(
        CsvRow row,
        string column,
        string sourceRecordId,
        int rowNumber,
        List<DirectoryImportIssue> errors,
        ref int identityErrors)
    {
        var value = row.Get(column);
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(Issue("missing-value", sourceRecordId, rowNumber, $"{column} is required."));
            identityErrors++;
            return string.Empty;
        }

        return value.Trim();
    }

    private static bool RequireBoolean(
        CsvRow row,
        string column,
        string sourceRecordId,
        int rowNumber,
        List<DirectoryImportIssue> errors,
        ref int identityErrors)
    {
        var value = row.Get(column);
        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        errors.Add(Issue("invalid-boolean", sourceRecordId, rowNumber, $"{column} must be true or false."));
        identityErrors++;
        return false;
    }

    private static DateTimeOffset RequireTimestamp(
        CsvRow row,
        string column,
        string sourceRecordId,
        int rowNumber,
        List<DirectoryImportIssue> errors,
        ref int identityErrors)
    {
        var value = row.Get(column);
        if (TryParseTimestamp(value, out var parsed))
        {
            return parsed;
        }

        errors.Add(Issue("invalid-timestamp", sourceRecordId, rowNumber, $"{column} must be a UTC instant."));
        identityErrors++;
        return DateTimeOffset.UnixEpoch;
    }

    private static bool RequireFreshness(
        CsvRow row,
        string sourceRecordId,
        int rowNumber,
        List<DirectoryImportIssue> errors,
        ref int identityErrors)
    {
        var value = row.Get("freshness_status");
        if (string.Equals(value, "current", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(value, "stale", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        errors.Add(Issue(
            "invalid-freshness",
            sourceRecordId,
            rowNumber,
            "freshness_status must be current or stale. Production freshness windows are REQUIRES_HOSPITAL_DECISION and are not invented here."));
        identityErrors++;
        return false;
    }

    private static void RequirePrefix(
        string value,
        string purpose,
        int rowNumber,
        List<DirectoryImportIssue> errors,
        ref int identityErrors)
    {
        if (SimulationEnvironmentPolicy.HasSyntheticPrefix(value))
        {
            return;
        }

        errors.Add(Issue("missing-synthetic-prefix", value, rowNumber, $"{purpose} values require a synthetic SIM- prefix."));
        identityErrors++;
    }

    private static bool Same(CsvRow left, CsvRow right, string column)
        => string.Equals(left.Get(column).Trim(), right.Get(column).Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool TryParseTimestamp(string value, out DateTimeOffset parsed)
    {
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out parsed)
            && parsed.Offset == TimeSpan.Zero;
    }

    private static string ComputeHash(
        string sourceRecordId,
        string firstName,
        string lastName,
        string simulationCode,
        string specialty,
        bool isActive,
        DateTimeOffset sourceUpdated,
        bool isStale,
        IReadOnlyList<NormalizedDirectoryRole> roles,
        IReadOnlyList<NormalizedDirectoryEndpoint> endpoints,
        IReadOnlyList<NormalizedDirectoryOnCall> onCall)
    {
        var builder = new StringBuilder();
        builder.Append(sourceRecordId).Append('|')
            .Append(firstName).Append('|')
            .Append(lastName).Append('|')
            .Append(simulationCode).Append('|')
            .Append(specialty).Append('|')
            .Append(isActive).Append('|')
            .Append(sourceUpdated.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)).Append('|')
            .Append(isStale);
        foreach (var role in roles.OrderBy(item => item.DepartmentCode, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append('|').Append(role.SiteCode).Append('/').Append(role.DepartmentCode).Append('/')
                .Append(role.Title).Append('/').Append(role.IsPrimary);
        }

        foreach (var endpoint in endpoints.OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append('|').Append(endpoint.Kind).Append('/').Append(endpoint.Label).Append('/').Append(endpoint.Value);
        }

        foreach (var assignment in onCall.OrderBy(item => item.StartsAtUtc))
        {
            builder.Append('|').Append(assignment.Tier).Append('/').Append(assignment.StartsAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static DirectoryImportIssue Issue(string code, string sourceRecordId, int? rowNumber, string message)
        => new(code, sourceRecordId, rowNumber, message);

    private static List<CsvInputRow> ReadRows(TextReader reader, List<DirectoryImportIssue> errors)
    {
        var rows = new List<CsvInputRow>();
        var rowNumber = 0;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            rowNumber++;
            if (TryParseLine(line, out var values))
            {
                rows.Add(new CsvInputRow(rowNumber, values.ToArray()));
            }
            else
            {
                errors.Add(Issue("malformed-csv", string.Empty, rowNumber, "CSV contains an unterminated quoted field."));
                rows.Add(new CsvInputRow(rowNumber, null));
            }
        }

        return rows;
    }

    private static bool TryParseLine(string line, out List<string> fields)
    {
        fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (inQuotes)
            {
                if (character == '"')
                {
                    if (index + 1 < line.Length && line[index + 1] == '"')
                    {
                        current.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(character);
                }
            }
            else if (character == '"')
            {
                inQuotes = true;
            }
            else if (character == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }

        if (inQuotes)
        {
            fields.Clear();
            return false;
        }

        fields.Add(current.ToString());
        return true;
    }

    private sealed record CsvInputRow(int RowNumber, IReadOnlyList<string>? Values);

    private sealed class CsvRow(IReadOnlyDictionary<string, int> header, IReadOnlyList<string> values)
    {
        public string Get(string column)
        {
            if (!header.TryGetValue(column, out var index) || index >= values.Count)
            {
                return string.Empty;
            }

            return values[index];
        }
    }
}
