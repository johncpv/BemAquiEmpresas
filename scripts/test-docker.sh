#!/usr/bin/env sh
# Roda build + testes .NET num container Linux (mesmo ambiente do CI).
# Útil no Windows com Smart App Control ativo, que bloqueia DLLs recém-compiladas.
# Uso: sh scripts/test-docker.sh [argumentos extras do dotnet test]
# BQ_TEST_IMAGE troca a imagem (ex.: mcr.microsoft.com/dotnet/sdk:10.0-alpine, para o PDF na Alpine, ADR 0022).
# Os testes de carga (Categoria=Carga, dezenas de milhares de linhas) ficam de fora; BQ_CARGA=1 inclui. O CI roda todos.
set -eu
ROOT=$(cd "$(dirname "$0")/.." && pwd -W 2>/dev/null || pwd)
if [ "${BQ_CARGA:-0}" != "1" ]; then
  set -- --filter-not-trait "Categoria=Carga" "$@"
fi
MSYS_NO_PATHCONV=1 docker run --rm \
  -v "$ROOT:/src" \
  -v bemaqui-nuget:/root/.nuget/packages \
  -v //var/run/docker.sock:/var/run/docker.sock \
  -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal \
  -e BQ_UPDATE_OPENAPI -e BQ_UPDATE_SNAPSHOT_SQL \
  -w /src "${BQ_TEST_IMAGE:-mcr.microsoft.com/dotnet/sdk:10.0}" \
  dotnet test --solution BemAquiEmpresas.slnx --artifacts-path /tmp/artifacts "$@"
