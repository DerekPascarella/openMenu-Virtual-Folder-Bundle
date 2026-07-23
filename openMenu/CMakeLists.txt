cmake_minimum_required(VERSION 3.19)
project(openmenu C)

include(GNUInstallDirs)
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/${CMAKE_INSTALL_BINDIR})
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/${CMAKE_INSTALL_LIBDIR})
set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/${CMAKE_INSTALL_LIBDIR})

set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)

# --- Options ---
option(BUILD_DREAMCAST "Build the OpenMenu target for Dreamcast (requires KOS toolchain)" ON)
option(BUILD_PC "Build the native host tools" OFF)

if (BUILD_PC)
    add_compile_options("-DSTANDALONE_BINARY=1")
endif ()

# --- Shared Dependencies ---
# UtHash (Header Only)
add_library(uthash INTERFACE)
target_include_directories(uthash INTERFACE
        ${CMAKE_SOURCE_DIR}/external/uthash/include
)
add_subdirectory(external/crayon_savefile)
add_subdirectory(external/easing)
add_subdirectory(external/ini)

add_subdirectory(src)

# --- Code Formatting (optional, requires clang-format) ---
find_program(CLANG_FORMAT clang-format)
if(CLANG_FORMAT)
    file(GLOB_RECURSE ALL_SOURCE_FILES
        ${CMAKE_SOURCE_DIR}/src/*.c
        ${CMAKE_SOURCE_DIR}/src/*.h
    )

    add_custom_target(format
        COMMAND ${CLANG_FORMAT} -i ${ALL_SOURCE_FILES}
        COMMENT "Formatting source files with clang-format"
        VERBATIM
    )

    add_custom_target(format-check
        COMMAND ${CLANG_FORMAT} --dry-run --Werror ${ALL_SOURCE_FILES}
        COMMENT "Checking source formatting with clang-format"
        VERBATIM
    )
endif()
