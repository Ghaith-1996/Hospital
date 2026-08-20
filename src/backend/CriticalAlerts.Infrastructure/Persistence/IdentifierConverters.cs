using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CriticalAlerts.Infrastructure.Persistence;

internal static class IdentifierConverters
{
    internal static ValueConverter<TId, Guid> For<TId>(Func<Guid, TId> factory, Func<TId, Guid> value)
        => new(id => value(id), guid => factory(guid));

    internal static void GuidId<TId>(this PropertyBuilder<TId> property, Func<Guid, TId> factory, Func<TId, Guid> value, string column)
        where TId : struct
    {
        property.HasColumnName(column).HasConversion(For(factory, value));
    }
}
