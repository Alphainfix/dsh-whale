#!/bin/bash
set -euo pipefail
exec bash "$(dirname "$0")/../mac/scripts/install-launchagent.sh" "$@"
