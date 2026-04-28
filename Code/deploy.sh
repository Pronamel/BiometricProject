#!/bin/bash

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

cd "$SCRIPT_DIR/Server"

echo ""
echo "================================================"
echo "PUBLISHING SERVER"
echo "================================================"
echo ""

dotnet publish

echo ""
echo "================================================"
echo "REMOVING UNNECESSARY FILES"
echo "================================================"
echo ""

cd bin/Release/net8.0/publish

# Remove debug symbols
rm -f Server.pdb *.pdb

# Remove Windows exe
rm -f Server.exe

# Remove CodeAnalysis compilation tools (6MB+)
rm -f Microsoft.CodeAnalysis*.dll
rm -f Humanizer.dll
rm -f Mono.TextTemplating.dll
rm -f System.CodeDom.dll
rm -f System.Composition*.dll

# Remove EntityFramework Design (migrations tooling)
rm -f Microsoft.EntityFrameworkCore.Design.dll
rm -f Microsoft.Extensions.DependencyModel.dll

# Remove Swagger UI (3MB)
rm -f Swashbuckle.AspNetCore.SwaggerUI.dll

# Remove localization folders (language resource folders)
for dir in cs de es fr it ja ko pl pt-BR ru tr zh-Hans zh-Hant; do
    [ -d "$dir" ] && rm -rf "$dir"
done

# Remove old leftovers
[ -d bin ] && rm -rf bin
[ -d publish ] && rm -rf publish
rm -f web.config

echo ""
echo "================================================"
echo "UPLOADING OPTIMIZED BUILD TO EC2"
echo "================================================"
echo ""

cd "$SCRIPT_DIR/Server"

echo "Securing private key permissions..."
KEY_PATH="$SCRIPT_DIR/Server-Key.pem"

if [ ! -f "$KEY_PATH" ]; then
    echo "ERROR: Key file not found at \"$KEY_PATH\""
    exit 1
fi

chmod 400 "$KEY_PATH"

scp -i "$KEY_PATH" -r bin/Release/net8.0/publish/* ec2-user@34.238.14.248:/home/ec2-user/serverSpace

echo ""
echo "================================================"
echo "DEPLOYMENT COMPLETE"
echo "================================================"
echo ""
