#!/usr/bin/env sh
# Falha se algum "uses:" dos workflows não estiver fixado por SHA completo (FND-01, CA 11).
set -eu
bad=$(grep -RhnE '^\s*-?\s*uses:\s*' .github/workflows | grep -vE 'uses:\s*\./' | grep -vE '@[0-9a-f]{40}(\s|$)' || true)
if [ -n "$bad" ]; then
  echo "Actions sem SHA fixo:"
  echo "$bad"
  exit 1
fi
echo "Todas as actions estão fixadas por SHA."
