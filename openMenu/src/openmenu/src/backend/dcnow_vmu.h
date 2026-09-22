/*
 * File: dcnow_vmu.h
 * Project: openmenu
 * Dreamcast Now! on the VMU LCD: the indicator icons and the player screen.
 */

#pragma once

/* Once per frame from the main loop. Cheap unless something changed. */
void dcnow_vmu_tick(void);

/* Redraws the current screen on every LCD, for a VMU that was just plugged in. */
void dcnow_vmu_redraw(void);

/* Paints the hang-up screen at once, for a teardown that runs before the
 * next tick. */
void dcnow_vmu_hanging_up(void);

/* Hands the LCD back to the library and repaints the icon, before a launch. */
void dcnow_vmu_release(void);
