# Shortcuts for cisc486. Run `just` to list them.

set windows-shell := ["powershell.exe", "-NoLogo", "-NoProfile", "-Command"]

# List the shortcuts
default:
    @just --list

# Install the tools (Windows). Lines that say "already installed" are fine.
[windows]
install:
    -winget install --exact --id Microsoft.DotNet.SDK.10
    -winget install --exact --id Unity.UnityHub

# Install the tools (macOS)
[macos]
install:
    brew install git-lfs
    brew install --cask dotnet-sdk unity-hub

# Turn on Git LFS and download the large files (run once after cloning)
setup:
    git lfs install
    git lfs pull

# Run the same checks as GitHub (stage your files with `git add` first)
check:
    dotnet run tools/RepoCheck.cs

# Fix C# formatting
format:
    dotnet run tools/RepoCheck.cs -- format

# Test local weapon trigger behavior without launching Unity
test-local:
    dotnet run --project tools/LocalGameplay.Tests/LocalGameplay.Tests.csproj
