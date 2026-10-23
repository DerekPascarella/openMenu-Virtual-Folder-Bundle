#ifndef OPENMENU_SAVEFILE_H
#define OPENMENU_SAVEFILE_H

#include <crayon_savefile/savefile.h>
#include <stdbool.h>
#include <time.h>
#include "sd_savefile.h"

void savefile_defaults();

uint8_t setup_savefile(crayon_savefile_details_t* details);

int8_t update_savefile(void** loaded_variables, crayon_savefile_version_t loaded_version,
                       crayon_savefile_version_t latest_version);

int8_t find_first_valid_savefile_device(crayon_savefile_details_t* details);
void savefile_init();
void savefile_close();

/* VMU LCD icon selection. Variant 0 is the plain set, 1 and 2 the Dreamcast
 * Now! offline and online sets. An owner draws the LCD itself and the library
 * then never restores the logo after a write. */
void savefile_set_lcd_variant(int variant);
const void* savefile_lcd_logo(void);
const void* savefile_lcd_access(void);
bool savefile_lcd_busy(void);
void savefile_set_lcd_busy(bool busy);
void savefile_set_lcd_owner(bool owned);
bool savefile_lcd_owned(void);

int8_t savefile_save();

/* Save/Load window helper functions */
int8_t savefile_get_device_status(int8_t device_id);
uint32_t savefile_get_device_version(int8_t device_id);
void savefile_refresh_device_info(void);
void savefile_refresh_single_device_info(int8_t device_id);
int8_t savefile_save_to_device(int8_t device_id);
int8_t savefile_load_from_device(int8_t device_id);
int8_t savefile_get_startup_device_id(void);
uint32_t savefile_get_save_size_blocks(void);
uint32_t savefile_get_device_free_blocks(int8_t device_id);

/* SD card support functions */
bool savefile_was_loaded_from_sd(void);
bool savefile_sd_available(void);
SD_STATUS savefile_get_sd_status(void);
bool savefile_sd_unreadable(void);
uint32_t savefile_get_sd_version(void);
int8_t savefile_save_to_sd(void);
int8_t savefile_load_from_sd(void);
void savefile_refresh_sd_status(void);

/* VMU time sync function */
int8_t sync_rtc_from_vmu(void);
bool vmu_time_sync_warning_pending(void);
void vmu_time_sync_warning_dismiss(void);

/* Positive status means the RTC was set but its checkpoint failed. */
typedef enum clock_sync_result {
    CLOCK_SYNC_OK = 0,
    CLOCK_SYNC_CHECKPOINT_FAILED = 1,
    CLOCK_SYNC_RTC_FAILED = -1,
    CLOCK_SYNC_STORAGE_FULL = -2,
    CLOCK_SYNC_STORAGE_ERROR = -3,
    CLOCK_SYNC_INVALID_TIME = -4
} clock_sync_result_t;

int8_t set_rtc_and_syscfg(time_t local_time);
bool clock_sync_storage_full(void);
bool clock_sync_warning_pending(void);
void clock_sync_warning_dismiss(void);
void clock_extender_tick(void);

#endif // OPENMENU_SAVEFILE_H
