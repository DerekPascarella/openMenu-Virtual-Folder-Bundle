/*
 * File: openmenu_debug.h
 * Project: openMenu
 *
 * Debug feature toggles. Set to 1 to enable.
 */

#ifndef OPENMENU_DEBUG_H
#define OPENMENU_DEBUG_H

/*
 * DEBUG_MAPLE_FLASH - Flash screen colors during boot to diagnose hangs
 *
 * When enabled, the screen will flash different colors at each stage of
 * maple device initialization. The last color shown indicates where a
 * hang occurs.
 *
 * Color sequence in main.c:
 *   RED (255,0,0)     - Before maple_wait_scan()
 *   GREEN (0,255,0)   - After maple_wait_scan()
 *   BLUE (0,0,255)    - Before vm2_rescan()
 *   YELLOW (255,255,0) - After vm2_rescan()
 *   CYAN (0,255,255)  - Before init_gfx_pvr()
 *   MAGENTA (255,0,255) - Before savefile_init()
 *   WHITE (255,255,255) - Init complete
 *
 * Color sequence in openmenu_savefile.c (after MAGENTA):
 *   Dark Blue (0,0,128) - Before setup_savefile_internal()
 *   Dark Yellow (128,128,0) - After setup_savefile_internal()
 *   Dark Cyan (0,128,128) - Before sd_savefile_init()
 *   Dark Magenta (128,0,128) - After sd_savefile_init()
 *   [If SD loads: return after next 2 flashes]
 *   Dark Red (128,0,0) - Before has_any_vmu()
 *   Dark Green (0,128,0) - VMU found / Orange (255,128,0) - No VMU
 *   [If SD failed, continue to VMU path:]
 *   Bright Pink (255,128,128) - Before find_first_valid_savefile_device()
 *   Light Green (128,255,128) - After find_first_valid_savefile_device()
 *
 * Each flash lasts 300ms, so boot will be ~4.5 seconds slower when enabled.
 */
#define DEBUG_MAPLE_FLASH     0

/*
 * DEBUG_COMPACTION_TEST - Enable flashrom partition compaction test menu
 *
 * When enabled, adds a hidden menu option to test the flashrom partition
 * compaction feature. Accessible via a specific button combo in the
 * settings menu (currently disabled - see ui_menu_credits.c menu_accept).
 *
 * WARNING: This test writes to flashrom! Use with caution. It backs up
 * the partition first and restores it after, but power loss during the
 * test could corrupt flashrom data.
 */
#define DEBUG_COMPACTION_TEST 0

/*
 * DEBUG_VMU_SYNC - Show VMU time sync debug overlay
 *
 * When enabled, displays detailed debug information about VMU clock
 * synchronization on screen. Shows:
 *   - Number of slots checked, memcards found, clocks found
 *   - Device index, port, and unit of found clock device
 *   - vmu_get_datetime result and time value
 *   - RTC set result and flashrom update result
 *   - Raw clock bytes from VMU response
 */
#define DEBUG_VMU_SYNC        0

#endif /* OPENMENU_DEBUG_H */
