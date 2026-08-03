#!/bin/bash
# Creates a proper macOS .app bundle from dotnet publish output
# Usage: ./create-macos-bundle.sh <publish_output_dir> <version> <output_dir> [arch]
# Arch defaults to "x64" if not specified.

set -e

# Ensure ~/.local/bin is in PATH (non-interactive shells don't source .bashrc)
export PATH="$HOME/.local/bin:$PATH"

PUBLISH_DIR=$1
VERSION=$2
OUTPUT_DIR=$3
ARCH=${4:-x64}

if [ -z "$PUBLISH_DIR" ] || [ -z "$VERSION" ] || [ -z "$OUTPUT_DIR" ]; then
    echo "Usage: $0 <publish_output_dir> <version> <output_dir> [arch]"
    exit 1
fi

APP_NAME="GDMENUCardManager"
BUNDLE_NAME="${APP_NAME}.app"
BUNDLE_PATH="${OUTPUT_DIR}/${BUNDLE_NAME}"

echo "Creating macOS app bundle: ${BUNDLE_NAME}"
echo "Version: ${VERSION}"
echo "Architecture: ${ARCH}"

# Create the app bundle structure
mkdir -p "${BUNDLE_PATH}/Contents/MacOS"
mkdir -p "${BUNDLE_PATH}/Contents/Resources"

# Copy all published files to Contents/MacOS
echo "Copying application files..."
cp -r "${PUBLISH_DIR}"/* "${BUNDLE_PATH}/Contents/MacOS/"

# Copy Info.plist and update version
echo "Creating Info.plist..."
if [ -f "src/${APP_NAME}.AvaloniaUI/Info.plist" ]; then
    cp "src/${APP_NAME}.AvaloniaUI/Info.plist" "${BUNDLE_PATH}/Contents/Info.plist"

    if [ "$(uname)" == "Darwin" ]; then
        sed -i '' "s/<string>1.0<\/string>/<string>${VERSION}<\/string>/g" "${BUNDLE_PATH}/Contents/Info.plist"
        sed -i '' "s/<string>1.0.0<\/string>/<string>${VERSION}.0<\/string>/g" "${BUNDLE_PATH}/Contents/Info.plist"
    else
        sed -i "s/<string>1.0<\/string>/<string>${VERSION}<\/string>/g" "${BUNDLE_PATH}/Contents/Info.plist"
        sed -i "s/<string>1.0.0<\/string>/<string>${VERSION}.0<\/string>/g" "${BUNDLE_PATH}/Contents/Info.plist"
    fi
else
    echo "Warning: Info.plist template not found at src/${APP_NAME}.AvaloniaUI/Info.plist"
fi

# Make the executable and native libraries executable
echo "Setting executable permissions..."
chmod +x "${BUNDLE_PATH}/Contents/MacOS/${APP_NAME}"
find "${BUNDLE_PATH}/Contents/MacOS" -name "*.dylib" -exec chmod +x {} \;

# Copy icon to Resources (must happen before signing so the manifest includes it)
if [ -f "src/${APP_NAME}.AvaloniaUI/Assets/icon.icns" ]; then
    cp "src/${APP_NAME}.AvaloniaUI/Assets/icon.icns" "${BUNDLE_PATH}/Contents/Resources/"
    echo "Icon file copied."
else
    echo "Warning: Icon file not found at src/${APP_NAME}.AvaloniaUI/Assets/icon.icns"
fi

# Ad-hoc code signing (required for Apple Silicon arm64 binaries to execute).
# Apple's codesign seals every bundle file and passes strict verification, so
# prefer it when building on a Mac. rcodesign covers WSL/Linux cross-builds but
# cannot seal the non Mach-O files in Contents/MacOS (apple-platform-rs issue
# 87), so cross-built archives fail strict verification until re-signed on a Mac.
echo "Ad-hoc code signing the bundle..."
if [ "$(uname)" == "Darwin" ] && command -v codesign &> /dev/null; then
    codesign --force --deep --sign - "${BUNDLE_PATH}"
    echo "Verifying signature..."
    codesign --verify --deep --strict --verbose=2 "${BUNDLE_PATH}"
elif command -v rcodesign &> /dev/null; then
    SIGN_RC=0
    SIGN_OUTPUT=$(rcodesign sign "${BUNDLE_PATH}" 2>&1) || SIGN_RC=$?
    echo "${SIGN_OUTPUT}" | grep -v "non Mach-O file\|we do not know how\|if the bundle signs" || true
    if [ ${SIGN_RC} -ne 0 ]; then
        echo "ERROR: rcodesign failed (exit code ${SIGN_RC})."
        exit 1
    fi
    echo "Note: this cross-built archive will not pass codesign --verify --deep --strict."
    echo "macOS users can re-seal it with: codesign --force --deep -s - ${BUNDLE_NAME}"
else
    echo "ERROR: No code signing tool found (rcodesign or codesign)."
    echo "Apple Silicon Macs require signed binaries. Install rcodesign:"
    echo "  https://github.com/indygreg/apple-platform-rs"
    exit 1
fi

echo "macOS app bundle created at: ${BUNDLE_PATH}"

# Create a tar.gz archive
echo "Creating tar.gz archive..."
cd "${OUTPUT_DIR}"
tar -czf "${APP_NAME}.${VERSION}-osx-${ARCH}-AppBundle.tar.gz" "${BUNDLE_NAME}"
cd - > /dev/null

# Clean up the .app directory (archive is the deliverable)
rm -rf "${BUNDLE_PATH}"

echo "Archive created: ${OUTPUT_DIR}/${APP_NAME}.${VERSION}-osx-${ARCH}-AppBundle.tar.gz"
echo "Done!"
