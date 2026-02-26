#ifndef OPENMENU_SAVEFILE_H
#define OPENMENU_SAVEFILE_H

#include <crayon_savefile/savefile.h>
#include <stdbool.h>
#include "sd_savefile.h"

void savefile_defaults();

uint8_t setup_savefile(crayon_savefile_details_t* details);

int8_t update_savefile(void** loaded_variables, crayon_savefile_version_t loaded_version,
                       crayon_savefile_version_t latest_version);

int8_t find_first_valid_savefile_device(crayon_savefile_details_t* details);
void savefile_init();
void savefile_close();
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
uint32_t savefile_get_sd_version(void);
int8_t savefile_save_to_sd(void);
int8_t savefile_load_from_sd(void);
void savefile_refresh_sd_status(void);

/* VMU time sync function */
int8_t sync_rtc_from_vmu(void);

/* VMU_SYNC_DEBUG_START */
#if 0
/* VMU time sync debug info - disabled, enable for debugging VMU clock issues */
const char* get_vmu_sync_debug_line1(void);
const char* get_vmu_sync_debug_line2(void);
const char* get_vmu_sync_debug_line3(void);
const char* get_vmu_sync_debug_line4(void);
const char* get_vmu_sync_debug_line5(void);
#endif
/* VMU_SYNC_DEBUG_END */

/* COMPACTION_TEST_START */
/* Compaction test functions - DEBUG ONLY, remove before release */
int8_t compaction_test_init(void);
int8_t compaction_test_step(void);
int8_t compaction_test_restore(void);
void compaction_test_cleanup(void);
int compaction_test_get_write_count(void);
int compaction_test_get_total_blocks(void);
int compaction_test_get_result(void);
const char* compaction_test_get_status(void);
/* COMPACTION_TEST_END */

#endif // OPENMENU_SAVEFILE_H
