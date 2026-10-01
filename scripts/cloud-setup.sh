#!/usr/bin/env bash
# SessionStart hook: makes sure the .NET 8 SDK is available in Claude Code cloud sessions.
# Does nothing on local machines (CLAUDE_CODE_REMOTE is only set in the cloud).
set -uo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ] && [ "${1:-}" != "--force" ]; then
    exit 0
fi

has_sdk8() {
    command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q '^8\.'
}

persist_env() {
    # Variables written to CLAUDE_ENV_FILE are applied to every later command in the session.
    if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
        {
            echo "export DOTNET_ROOT=\"$1\""
            echo "export PATH=\"$1:\$PATH\""
            echo "export DOTNET_CLI_TELEMETRY_OPTOUT=1"
            echo "export DOTNET_NOLOGO=1"
        } >> "$CLAUDE_ENV_FILE"
    fi
}

if [ -x "$HOME/.dotnet/dotnet" ]; then
    export PATH="$HOME/.dotnet:$PATH"
    export DOTNET_ROOT="$HOME/.dotnet"
fi

if ! has_sdk8; then
    echo "Installing .NET 8 SDK..." >&2
    SUDO=""
    [ "$(id -u)" -ne 0 ] && command -v sudo >/dev/null 2>&1 && SUDO="sudo -n"

    # Ubuntu ships dotnet-sdk-8.0 in its own archive, which is reachable from the default cloud network.
    if command -v apt-get >/dev/null 2>&1; then
        $SUDO apt-get update -qq >/dev/null 2>&1 && $SUDO apt-get install -y -qq dotnet-sdk-8.0 >/dev/null 2>&1
    fi

    # Fallback: Microsoft's install script into ~/.dotnet (needs access to dot.net / builds.dotnet.microsoft.com).
    if ! has_sdk8; then
        curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
            && bash /tmp/dotnet-install.sh --channel 8.0 --install-dir "$HOME/.dotnet" >/dev/null 2>&1
        export PATH="$HOME/.dotnet:$PATH"
        export DOTNET_ROOT="$HOME/.dotnet"
    fi
fi

if has_sdk8; then
    persist_env "$(dirname "$(readlink -f "$(command -v dotnet)")")"
    # Warm the NuGet cache so the first build in the session is quick.
    (cd "${CLAUDE_PROJECT_DIR:-$(dirname "$0")/..}" && dotnet restore RapidTrigger.sln >/dev/null 2>&1) || true
    echo ".NET SDK ready: $(dotnet --version)" >&2
else
    echo "Could not install the .NET 8 SDK. Allow network access to the Ubuntu archive or dot.net in the cloud environment settings." >&2
fi

exit 0
