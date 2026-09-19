#!/usr/bin/env bash
# Shared host detection for the repository validation scripts. Source this file; do not run it.

# Prints the .NET runtime identifier for the current host. RUNTIME_IDENTIFIER overrides detection.
litedb_runtime_identifier() {
    if [ -n "${RUNTIME_IDENTIFIER:-}" ]; then
        printf '%s\n' "$RUNTIME_IDENTIFIER"
        return 0
    fi

    local kernel machine operating_system architecture

    kernel="$(uname -s)"
    machine="$(uname -m)"

    case "$kernel" in
        Linux) operating_system="linux" ;;
        Darwin) operating_system="osx" ;;
        MINGW*|MSYS*|CYGWIN*|Windows_NT) operating_system="win" ;;
        *)
            printf 'Unsupported host operating system: %s\n' "$kernel" >&2
            return 1
            ;;
    esac

    case "$machine" in
        x86_64|amd64) architecture="x64" ;;
        arm64|aarch64) architecture="arm64" ;;
        *)
            printf 'Unsupported host architecture: %s\n' "$machine" >&2
            return 1
            ;;
    esac

    printf '%s-%s\n' "$operating_system" "$architecture"
}

# Prints the executable file name extension used by the given runtime identifier.
litedb_executable_suffix() {
    case "$1" in
        win-*) printf '%s\n' '.exe' ;;
        *) printf '%s\n' '' ;;
    esac
}

# Prints a path the .NET CLI can resolve. Git Bash on Windows uses POSIX paths that the CLI and
# MSBuild do not understand, so convert them there; every other host passes the path through.
litedb_native_path() {
    if command -v cygpath >/dev/null 2>&1; then
        cygpath -w "$1"
    else
        printf '%s\n' "$1"
    fi
}
