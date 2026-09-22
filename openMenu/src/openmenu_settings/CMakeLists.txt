add_library(openmenu_settings
        STATIC
        src/openmenu_savefile.c
        src/openmenu_settings.c
        src/sd_savefile.c
        src/vmu_sync_debug.c
)
target_sources(openmenu_settings
        PUBLIC
        include/openmenu_lcd.h
        include/openmenu_pal.h
        include/openmenu_savefile.h
        include/openmenu_settings.h
        include/openmenu_vmu.h
        include/sd_savefile.h
)

target_include_directories(openmenu_settings
        PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
        PRIVATE
        src
)

# Link against crayon_savefile and kosfat (for SD card FAT filesystem support)
target_link_libraries(openmenu_settings PRIVATE crayon_savefile kosfat)
