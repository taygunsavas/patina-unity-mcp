#!/usr/bin/env bash
# Bash equivalent of publish-dev-runtime.ps1 for contributors without PowerShell 7.
# Both scripts must resolve the same platform directory names as
# ProcessManager.GetPlatformDirectory() in the Unity package.

set -euo pipefail

binary_path=""
output_root="dist/dev-runtime/current"

usage() {
    cat <<'USAGE'
Usage: publish-dev-runtime.sh [--binary-path <path>] [--output-root <path>]

  --binary-path   Server binary to publish.
                  Default: rust-server/target/release/patina-server[.exe]
  --output-root   Development runtime root.
                  Default: dist/dev-runtime/current

Relative paths are resolved against the repository root, not the current
working directory.
USAGE
}

while [ $# -gt 0 ]; do
    case "$1" in
        --binary-path)
            [ $# -ge 2 ] || { echo "Missing value for --binary-path" >&2; exit 1; }
            binary_path="$2"
            shift 2
            ;;
        --output-root)
            [ $# -ge 2 ] || { echo "Missing value for --output-root" >&2; exit 1; }
            output_root="$2"
            shift 2
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            echo "Unknown argument: $1" >&2
            usage >&2
            exit 1
            ;;
    esac
done

get_platform_directory() {
    case "$(uname -s)" in
        Darwin)
            if [ "$(uname -m)" = "arm64" ]; then
                echo "aarch64-macos"
            else
                echo "x86_64-macos"
            fi
            ;;
        MINGW*|MSYS*|CYGWIN*)
            echo "x86_64-win"
            ;;
        *)
            echo "x86_64-linux"
            ;;
    esac
}

get_binary_extension() {
    case "$(uname -s)" in
        MINGW*|MSYS*|CYGWIN*) echo ".exe" ;;
        *) echo "" ;;
    esac
}

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"

binary_extension="$(get_binary_extension)"
platform_directory="$(get_platform_directory)"

if [ -z "$binary_path" ]; then
    binary_path="rust-server/target/release/patina-server${binary_extension}"
fi

if [ ! -f "$binary_path" ]; then
    echo "Server binary not found: $binary_path" >&2
    echo "Run 'cargo build --release' in rust-server/ first." >&2
    exit 1
fi

resolved_binary="$(cd "$(dirname "$binary_path")" && pwd)/$(basename "$binary_path")"
case "$output_root" in
    /*) resolved_output_directory="$output_root/$platform_directory" ;;
    *) resolved_output_directory="$repository_root/$output_root/$platform_directory" ;;
esac

mkdir -p "$resolved_output_directory"

destination_path="$resolved_output_directory/patina-server${binary_extension}"

# Publish through a temporary file and rename(2) instead of overwriting in place.
# On macOS, writing over a binary that an MCP host is still running keeps the same
# inode and invalidates the kernel's code-signing pages, so every later exec() of
# that path is SIGKILLed (exit 137). A rename allocates a fresh inode and leaves the
# running process untouched. See issue #99.
temporary_path="$destination_path.tmp-$$"
cleanup() { rm -f "$temporary_path"; }
trap cleanup EXIT

cp -f "$resolved_binary" "$temporary_path"
chmod +x "$temporary_path"
mv -f "$temporary_path" "$destination_path"

echo "Development runtime published:"
echo "  Source      : $resolved_binary"
echo "  Destination : $destination_path"
