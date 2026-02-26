/*
 * File: sd_savefile.h
 * Project: openmenu_settings
 * Description: Serial SD card save/load support for openMenu settings
 */

#ifndef SD_SAVEFILE_H
#define SD_SAVEFILE_H

#include <stdbool.h>
#include <stdint.h>

#ifdef _arch_dreamcast

/* SD card configuration file path */
#define SD_MOUNT_PATH       "/sd"
#define SD_OPENMENU_DIR     "/sd/OPENMENU"
#define SD_CONFIG_FILE      "/sd/OPENMENU/OPENMENU.CFG"

/* File format magic and version */
#define SD_CONFIG_MAGIC     "OMCF"
#define SD_CONFIG_MAGIC_LEN 4

/* SD device status codes (mirrors VMU SAVE_STATUS for consistency) */
typedef enum SD_STATUS {
    SD_STATUS_NOT_PRESENT = 0, /* SD card not detected or init failed */
    SD_STATUS_NO_FILE,         /* SD present but no config file */
    SD_STATUS_READY,           /* SD present with valid current-version config */
    SD_STATUS_OLD,             /* SD has older version config (will upgrade) */
    SD_STATUS_INVALID,         /* SD has corrupt/invalid config file */
    SD_STATUS_FUTURE,          /* SD has config from newer program version */
    SD_STATUS_NO_SPACE         /* SD card is full */
} SD_STATUS;

/* SD card save file header structure */
typedef struct sd_config_header {
    char magic[4];      /* "OMCF" */
    uint32_t version;   /* SFV_CURRENT */
    uint32_t data_size; /* Size of settings data */
    uint32_t checksum;  /* Simple checksum for validation */
} sd_config_header_t;

int8_t sd_savefile_init(void); /* returns -1 on failure but doesn't hang */
void sd_savefile_shutdown(void);
bool sd_savefile_available(void);
SD_STATUS sd_savefile_get_status(void);
uint32_t sd_savefile_get_version(void); /* 0 if no file exists */
int8_t sd_savefile_load(void);
int8_t sd_savefile_save(void);
void sd_savefile_refresh_status(void);

#else

/* Non-Dreamcast stubs */
typedef enum SD_STATUS { SD_STATUS_NOT_PRESENT = 0 } SD_STATUS;

#endif /* _arch_dreamcast */

#endif /* SD_SAVEFILE_H */
