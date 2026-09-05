param(
    [string]$WebImage = "critical-alerts-web:verification",
    [string]$ApiHost = "api"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

# Exercise the built Next.js proxy against a separate, synthetic HTTP fixture.
# No host ports, existing containers, credentials, or application data are used.
$suffix = [Guid]::NewGuid().ToString("N")
$network = "critical-alerts-proxy-$suffix"
$apiContainer = "critical-alerts-proxy-api-$suffix"
$webContainer = "critical-alerts-proxy-web-$suffix"
$createdNetwork = $false
$createdApi = $false
$createdWeb = $false

try {
    docker network create $network | Out-Null
    $createdNetwork = $true
    $fixture = @'
require('node:http').createServer((request, response) => {
  response.setHeader('Content-Type', 'application/json');
  response.end(JSON.stringify({ simulation: true, path: request.url }));
}).listen(8080, '0.0.0.0');
'@
    docker run --detach --network $network --network-alias $ApiHost --name $apiContainer --entrypoint node $WebImage -e $fixture | Out-Null
    $createdApi = $true
    docker run --detach --network $network --name $webContainer $WebImage | Out-Null
    $createdWeb = $true

    $probe = @'
(async () => {
  let ready = false;
  for (let attempt = 0; attempt < 60; attempt++) {
    try {
      const response = await fetch('http://127.0.0.1:3000', { signal: AbortSignal.timeout(1000) });
      if (response.ok) { ready = true; break; }
    } catch {}
    await new Promise(resolve => setTimeout(resolve, 500));
  }
  if (!ready) throw new Error('Web container did not become ready.');
  const response = await fetch('http://127.0.0.1:3000/api/v1/proxy-verification?fixture=synthetic', { signal: AbortSignal.timeout(5000) });
  if (!response.ok) throw new Error(`API proxy returned ${response.status}.`);
  const body = await response.json();
  if (body.simulation !== true || body.path !== '/api/v1/proxy-verification?fixture=synthetic') {
    throw new Error('API proxy did not reach the separate synthetic fixture with the original path and query.');
  }
  console.log('Built web container forwards API requests to the separate API container.');
})().catch(error => { console.error(error.message); process.exit(1); });
'@
    docker exec $webContainer node -e $probe
}
finally {
    if ($createdWeb) { docker rm --force $webContainer | Out-Null }
    if ($createdApi) { docker rm --force $apiContainer | Out-Null }
    if ($createdNetwork) { docker network rm $network | Out-Null }
}
