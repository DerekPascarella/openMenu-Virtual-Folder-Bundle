# KallistiOS Patch: Add GDFS Support to fs_iso9660

## Overview

This patch modifies the `fs_iso9660.c` filesystem implementation within the
KallistiOS (KOS) kernel. Its purpose is to add basic support for reading the
high-density area (second session) of GD-ROM discs, often referred to as GDFS
(Giga Disk File System), using the standard ISO9660 filesystem functions
(`iso_open`, `iso_read`, etc.).

This allows applications built with the patched KOS to access files stored in
the GD-ROM's high-density area without needing a separate GDFS driver or
implementation, provided the disc structure is compatible.

## Prerequisites

- A local copy of the KallistiOS source code.
- Git installed in your KOS development environment (e.g., within the Docker
  container or on your host system if building natively).
- A working KallistiOS build environment capable of compiling the KOS kernel
  (`make`).

## Instructions

Follow these steps to apply the patch and rebuild KOS:

1.  **Identify KOS Source Directory:** Determine the path to your KallistiOS
    source code. In many common setups (like the official KOS Docker image),
    this is `/opt/toolchains/dc/kos/`. Replace `<KOS_BASE_DIR>` in the commands
    below with your actual path.

2.  **Copy Patch File:** Copy the `fs_iso9660.patch` file into the root of your
    KOS source directory.

    ```bash
    cp fs_iso9660.patch <KOS_BASE_DIR>/
    ```

    _Example:_

    ```bash
    cp fs_iso9660.patch /opt/toolchains/dc/kos/
    ```

3.  **Navigate to KOS Directory:** Change your current directory to the KOS
    source directory.

    ```bash
    cd <KOS_BASE_DIR>
    ```

    _Example:_

    ```bash
    cd /opt/toolchains/dc/kos/
    ```

4.  **Apply the Patch:** Use Git to apply the patch file.

    ```bash
    git apply fs_iso9660.patch
    ```

    _Note:_ If this command reports errors or conflicts, it might mean the
    `fs_iso9660.c` file in your KOS version has diverged significantly from the
    one the patch was based on. You may need to resolve these conflicts manually
    or adapt the patch. Check `git status` for details after attempting to
    apply.

5.  **Rebuild KallistiOS:** Compile the modified KOS kernel and libraries.
    ```bash
    make
    ```
    _Tip:_ If you encounter unexpected build issues after patching, try cleaning
    the build first: `make clean && make`.

Once the `make` command completes successfully, KallistiOS will be rebuilt with
the GDFS support integrated into the `fs_iso9660` **filesystem implementation**.
Any applications subsequently compiled against this patched KOS will inherit
this capability.

## Patch Details

This patch achieves GDFS support within the existing ISO9660 filesystem
implementation through the following key modifications to `fs_iso9660.c`:

1.  **Global `disc_type` Variable:**

    - `+static int disc_type;`
    - A static global variable `disc_type` is introduced. This variable is
      intended to store the type of disc detected (e.g., `CD_ROM` or
      `CD_GDROM`), presumably set by the lower-level CD/GD-ROM hardware driver
      during initialization or status checks.

2.  **Conditional TOC Reading:**

    - `cdrom_read_toc(&toc, (disc_type == CD_GDROM))`
    - The call to `cdrom_read_toc` within the `init_percd` function is modified.
      The second parameter now conditionally passes `1` (true) if `disc_type`
      indicates a GD-ROM, and `0` (false) otherwise. This tells the underlying
      function to read the Table of Contents (TOC) for the _second session_
      (high-density area) when a GD-ROM is present, instead of the default first
      session.

3.  **Hardcoded Session Base for GD-ROM:**

    - `if(disc_type == CD_GDROM)`
    - `    session_base = 45150;`
    - `else if(!(session_base = cdrom_locate_data_track(&toc)))`
    - This is the core change. If the `disc_type` indicates a GD-ROM, the
      `session_base` variable (which represents the starting sector of the
      filesystem data) is explicitly set to `45150`. This is the standard
      starting logical block address (LBA) for the high-density data track on
      GD-ROMs.
    - If the disc is _not_ a GD-ROM, the original logic of finding the data
      track based on the (first session) TOC is used.

4.  **Removal of Local `disc_type`:**
    - The local `disc_type` variable within the `iso_vblank` function is
      removed, indicating reliance on the new global `disc_type` variable for
      status checks.

**In summary:** The patch leverages the existing ISO9660 parsing logic. By
detecting a GD-ROM, reading its second session TOC, and crucially, forcing the
filesystem's starting point (`session_base`) to the known beginning of the
high-density area (sector 45150), it allows the standard ISO9660 functions to
operate directly on the GDFS area as if it were a standard ISO9660 volume
starting at that offset.

---

# KallistiOS Patch: Fix LFN Handling in libkosfat

## Overview

This patch modifies `directory.c` within the `libkosfat` addon
(`addons/libkosfat/`), which is KallistiOS's FAT12/16/32 filesystem driver
with long filename (LFN) support. It corrects two bugs in the way LFN directory
entries are read and written:

1. **Off-by-one null terminator** in `fat_search_long`: when reading LFN
   entries, the null terminator was written one position past the end of the
   valid 13-character slot, leaving the correct position uninitialised.
2. **Incorrect LFN padding** in `fat_add_dentry`: when writing a partial LFN
   entry (i.e. one that doesn't fill all 13 UCS-2 character slots), the unused
   slots were filled with `0x0000` instead of `0xFFFF` as required by the FAT
   specification.

Without these fixes, filenames longer than 13 characters may fail to round-trip
correctly. The padding bug in particular can cause conformant FAT implementations
on host operating systems (including Windows, Linux, and macOS) to discard the
LFN entries and fall back to the 8.3 short filename instead.

## Prerequisites

- A local copy of the KallistiOS source code, including the `addons/` directory
  (libkosfat ships as part of the main KOS repository).
- Git installed in your KOS development environment (e.g., within the Docker
  container or on your host system if building natively).
- A working KallistiOS build environment with the KOS environment script already
  sourced (`environ.sh`), as libkosfat must be compiled with the Dreamcast
  cross-compiler.

## Instructions

Follow these steps to apply the patch and rebuild libkosfat:

1.  **Identify KOS Source Directory:** Determine the path to your KallistiOS
    source code. In many common setups (like the official KOS Docker image),
    this is `/opt/toolchains/dc/kos/`. Replace `<KOS_BASE_DIR>` in the commands
    below with your actual path.

2.  **Copy Patch File:** Copy the `libkosfat_directory.patch` file into the
    root of your KOS source directory.

    ```bash
    cp libkosfat_directory.patch <KOS_BASE_DIR>/
    ```

    _Example:_

    ```bash
    cp libkosfat_directory.patch /opt/toolchains/dc/kos/
    ```

3.  **Navigate to KOS Directory:** Change your current directory to the KOS
    source directory.

    ```bash
    cd <KOS_BASE_DIR>
    ```

    _Example:_

    ```bash
    cd /opt/toolchains/dc/kos/
    ```

4.  **Apply the Patch:** Use Git to apply the patch file.

    ```bash
    git apply libkosfat_directory.patch
    ```

    _Note:_ If this command reports errors or conflicts, it might mean the
    `directory.c` file in your KOS version has diverged significantly from the
    one the patch was based on. You may need to resolve these conflicts manually
    or adapt the patch. Check `git status` for details after attempting to
    apply.

5.  **Rebuild KallistiOS:** Compile the modified KOS kernel and libraries,
    which includes the libkosfat addon.
    ```bash
    make
    ```
    _Tip:_ If you encounter unexpected build issues after patching, try cleaning
    the build first: `make clean && make`.

Once the `make` command completes successfully, libkosfat will be rebuilt with
both fixes applied. Any applications subsequently compiled against this patched
KOS that use the FAT filesystem driver will inherit the corrected behaviour.

## Patch Details

This patch corrects two independent bugs in `addons/libkosfat/directory.c`:

1.  **Off-by-one null terminator (`fat_search_long`, line 287):**

    - `-            longname_buf[fnlen + 14] = 0;`
    - `+            longname_buf[fnlen + 13] = 0;`
    - Each LFN directory entry holds exactly 13 UCS-2 characters, spread across
      three name fields: `name1` (5 chars, indices `fnlen+0`–`fnlen+4`),
      `name2` (6 chars, indices `fnlen+5`–`fnlen+10`), and `name3` (2 chars,
      indices `fnlen+11`–`fnlen+12`). After copying all three fields, the null
      terminator must be placed at `fnlen+13` — immediately past the last valid
      character. The original code wrote to `fnlen+14`, skipping index 13 and
      leaving it uninitialised, which could cause the subsequent
      `fat_strlen_ucs2` call to compute an incorrect filename length.

2.  **Incorrect LFN padding (`fat_add_dentry`, line 1325):**

    - `-            memset(longname_buf2 + len, 0, 13 * sizeof(uint16_t));`
    - `+            memset(longname_buf2 + len, 0xFF, 13 * sizeof(uint16_t));`
    - `+            longname_buf2[len] = 0x0000;`
    - When a filename does not fill all 13 character slots of its final LFN
      entry, the FAT specification (Microsoft FAT Specification, section 7.3)
      requires that character positions after the null terminator be filled with
      `0xFFFF`. The original code used `memset(..., 0, ...)`, which filled those
      positions with `0x0000` instead.
    - The fix first fills the entire trailing region with `0xFF` bytes (so each
      `uint16_t` becomes `0xFFFF`), then writes the null terminator back at
      position `len` with `longname_buf2[len] = 0x0000`, restoring the correct
      structure.
    - Filenames that exactly fill one or more LFN entries with no partial
      trailing entry are unaffected by this bug.

**In summary:** Both bugs affect long filenames (longer than 13 characters) that
span more than one LFN directory entry. The off-by-one error corrupts reads of
such filenames; the padding error corrupts writes, potentially causing host
operating systems to ignore the LFN chain and display the 8.3 fallback name
instead.

# Addon Libraries openMenu Links

## Overview

openMenu links two KallistiOS addon libraries: `libkosfat.a` (the FAT driver
behind the SD card savefile, patched as described above) and `libppp.a` (the
PPP stack behind the Dreamcast Now! modem connection). Both are built by the
normal KOS `make` and land in `addons/lib/dreamcast/`. The link step fails with
`cannot find -lkosfat` or `cannot find -lppp` when they are missing.

## Keeping the Archives

`make clean` at the KOS root deletes the addon archives along with the objects,
so a tree that was cleaned and not rebuilt cannot link openMenu. The Dockerfile
in this folder therefore cleans only `kernel` and `utils` and deletes the addon
objects by hand. To restore the archives in an existing tree:

```bash
cd /opt/toolchains/dc/kos/
source environ.sh
make -C addons
```

## libppp

The stock `addons/libppp/Makefile` builds with `PPP_DEBUG` defined. Leave it
that way. The debug output only goes to the serial console, and turning it off
makes the stock sources fail under `-Werror` because of variables that are then
unused.

# KallistiOS Patch: Japanese Mouse Center Button

`mouse_middle_button.patch` adds `MOUSE_MIDDLEBUTTON` for bit 0 in
`kernel/arch/dreamcast/include/dc/maple/mouse.h` and changes the cooked button
mask from 14 to 15 in `kernel/arch/dreamcast/hardware/maple/mouse.c`. The Japanese
HKT-9900 center button uses bit 0. The western side button uses bit 3. OpenMenu
accepts either as its third-button action. Polarity and axis conversion stay
unchanged.

OpenMenu requires the patched header at compile time. The KOS library must also
be rebuilt, then OpenMenu must be rebuilt and relinked. A header change alone
cannot restore a button bit discarded by the linked driver.

## Existing Containers

Run the patch and compilation manually in a root shell inside the container.
KOS can retain its image-build owner's UID while the development user's UID
is changed by the devcontainer. In the inspected container, `dev` is UID 1000,
KOS is owned by UID 100, and `sudo` is absent.

From the Docker host, use `docker exec -it --user root CONTAINER bash`, replacing
`CONTAINER` with your container's name or ID. Then run this inside that shell,
with OpenMenu mounted at `/workspaces/openmenu`:

```bash
cd /opt/toolchains/dc/kos
source ./environ.sh

patch --dry-run --batch --forward -p1 < /workspaces/openmenu/docker/kos_patch/mouse_middle_button.patch &&
patch --batch --forward -p1 < /workspaces/openmenu/docker/kos_patch/mouse_middle_button.patch &&
kos-cc $KOS_CSTD -Wextra -Wno-deprecated \
    -c kernel/arch/dreamcast/hardware/maple/mouse.c \
    -o kernel/build/mouse.o &&
kos-ar rcs lib/dreamcast/libkallisti.a kernel/build/mouse.o
```

This procedure uses `patch` without a Git dependency. Only `mouse.c` needs
recompiling. The header adds a constant without changing any structure or
function ABI. `kos-cc` invokes the configured SH-4 compiler with KOS's flags;
`$KOS_CSTD` and the warning flags match the kernel and root Makefiles.
`kos-ar rcs` replaces the existing `mouse.o` member and updates the symbol
index in `libkallisti.a`, preserving its other members. No addon rebuild or
KOS-root clean is needed. `libkosfat.a` and `libppp.a` remain untouched.

Afterward, leave the root shell and rebuild and relink OpenMenu using your
normal container workflow. An existing OpenMenu executable still contains
its previously linked KOS driver.

If the forward check fails, inspect the error before applying anything. This
read-only check identifies a fully applied source patch:

```bash
patch --dry-run --batch --reverse -p1 < /workspaces/openmenu/docker/kos_patch/mouse_middle_button.patch
```

A successful reverse dry run means the source already contains the changes.
It does not prove the library was rebuilt. Do not actually reverse the patch.
If it is already fully applied, run only the compilation and archive commands
after sourcing the environment. If both checks fail, inspect for a partial
patch or upstream changes before continuing.

## Images and Input Sampling

The Dockerfile copies and applies this patch before building KOS. Editing the
Dockerfile does not update an existing image or running container. A prebuilt
image such as `sbstnc/openmenu-dev:0.2.2` still needs the manual procedure unless
it has already been patched and rebuilt. Building or publishing a replacement
image and changing a devcontainer image reference are separate user actions.

OpenMenu takes an interrupt-protected snapshot of the selected mouse's current
cooked state and clears its relative deltas once per input pass. Status reads
alone do not consume these deltas. The driver retains only its latest sample,
so intermediate movement or wheel samples can be lost while the UI is stalled
or blocked. This patch does not add a motion queue or change that driver
behavior.

OpenMenu uses KOS's single application detach callback slot, filtered to mouse
devices, to detect same-port reconnects between input passes. No other
OpenMenu code currently registers that callback. A future application detach
handler must share that registration. Third-button mapping, wheel polarity,
movement sensitivity, and cursor readability still require real mouse and
display checks.
