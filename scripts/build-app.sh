#!/bin/bash
set -euo pipefail
exec bash "$(dirname "$0")/../mac/scripts/build-app.sh" "$@"
