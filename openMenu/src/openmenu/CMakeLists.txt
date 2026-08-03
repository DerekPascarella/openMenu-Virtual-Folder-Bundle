# Read VERSION.TXT from repo root at build time
set(VERSION_FILE "${CMAKE_SOURCE_DIR}/VERSION.TXT")
if(EXISTS "${VERSION_FILE}")
    file(READ "${VERSION_FILE}" OPENMENU_VERSION)
    string(STRIP "${OPENMENU_VERSION}" OPENMENU_VERSION)
    message(STATUS "openMenu version: ${OPENMENU_VERSION}")
else()
    set(OPENMENU_VERSION "Unknown")
    message(WARNING "VERSION.TXT not found, using default version string")
endif()

set(OPENMENU_SOURCES
        src/main.c
        src/backend/bgm.c
        src/backend/boot_defaults.c
        src/backend/gdemu_control.c
        src/backend/gdemu_sdk.c
        src/backend/last_game.c
        src/texture/block_pool.c
        src/texture/lru.c
        src/texture/simple_texture_allocator.c
        src/texture/txr_manager.c
        src/ui/dc/font_bitmap.c
        src/ui/dc/font_bmf.c
        src/ui/dc/font_smooth.c
        src/ui/dc/input.c
        src/ui/dc/pvr_texture.c
        src/ui/animation.c
        src/ui/draw_kos.c
        src/ui/marquee.c
        src/ui/theme_manager.c
        src/ui/ui_grid.c
        src/ui/ui_line_desc.c
        src/ui/ui_line_large.c
        src/ui/ui_menu_credits.c
        src/ui/ui_scroll.c
        src/ui/ui_folders.c
        src/vm2/vm2_api.c
)

add_executable(openmenu ${OPENMENU_SOURCES})

# Pass version string as compile definition
target_compile_definitions(openmenu PRIVATE OPENMENU_BUILD_VERSION="${OPENMENU_VERSION}")

target_include_directories(openmenu
        PRIVATE
        src
)

target_link_libraries(openmenu
        PRIVATE
        crayon_savefile
        easing
        ini
        openmenu_settings
        openmenu_shared
        uthash
)

if (NOT DEFINED KOS_OBJCOPY)
    # Try to find the program using find_program first (more robust than env var)
    # KOS often uses prefixes, check KOS docs for the exact name (e.g., sh-elf-objcopy)
    find_program(KOS_OBJCOPY_PATH NAMES sh-elf-objcopy ${KOS_CC_PREFIX}objcopy HINTS ENV KOS_BIN_DIR ENV KOS_BASE/bin ENV KOS_BASE/toolchains/dc/sh-elf/bin)

    if (KOS_OBJCOPY_PATH)
        set(KOS_OBJCOPY ${KOS_OBJCOPY_PATH} CACHE FILEPATH "Path to KOS objcopy tool")
        message(STATUS "Found KOS objcopy: ${KOS_OBJCOPY}")
        # Fallback to environment variable if find_program fails
    elseif (DEFINED ENV{KOS_OBJCOPY})
        set(KOS_OBJCOPY $ENV{KOS_OBJCOPY} CACHE FILEPATH "Path to KOS objcopy tool from ENV{KOS_OBJCOPY}")
        message(STATUS "Using KOS objcopy from environment variable: ${KOS_OBJCOPY}")
    else ()
        message(FATAL_ERROR "KOS objcopy tool not found. Set KOS_OBJCOPY environment variable or ensure it's in PATH, or adjust find_program HINTS.")
    endif ()
endif ()

if (CMAKE_RUNTIME_OUTPUT_DIRECTORY)
    set(OPENMENU_BIN_OUTPUT ${CMAKE_RUNTIME_OUTPUT_DIRECTORY}/1ST_READ.BIN)
else ()
    set(OPENMENU_BIN_OUTPUT ${CMAKE_CURRENT_BINARY_DIR}/1ST_READ.BIN)
endif ()

add_custom_target(openmenu_bin ALL
        COMMAND
        ${KOS_OBJCOPY}
        -R .stack
        -O binary
        $<TARGET_FILE:openmenu>
        ${OPENMENU_BIN_OUTPUT}
        DEPENDS
        openmenu
        BYPRODUCTS
        ${OPENMENU_BIN_OUTPUT}
        COMMENT "Converting openmenu.elf to 1ST_READ.BIN"
        VERBATIM
)