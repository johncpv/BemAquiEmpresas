# Matriz completa do relatório: máquina normal, e Job Object com lentidão 4x e 6x (2 núcleos lógicos).
# O servidor roda FORA do job (só o navegador e o roteiro ficam limitados).
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$server = Start-Process -FilePath node -ArgumentList '--no-warnings', 'server.mjs', '--reset' -NoNewWindow -PassThru
Start-Sleep -Seconds 2
$env:SPK03_EXTERNAL_SERVER = '1'
try {
  node run-bench.mjs --rates=1 --techs=idb,idb-lean,sqlite --label=normal --out=bench-normal.json
  powershell -NoProfile -ExecutionPolicy Bypass -File lib\job-throttle.ps1 -Slowdown 4 -Cores 2 node run-bench.mjs --rates=1 --techs=idb,idb-lean,sqlite --label=job4x --out=bench-job4x.json
  powershell -NoProfile -ExecutionPolicy Bypass -File lib\job-throttle.ps1 -Slowdown 6 -Cores 2 node run-bench.mjs --rates=1 --techs=idb,idb-lean,sqlite --label=job6x --out=bench-job6x.json
} finally {
  Stop-Process -Id $server.Id -Force
}
