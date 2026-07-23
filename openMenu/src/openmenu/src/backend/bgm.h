/*
 * File: bgm.h
 * Project: backend
 * Description: Background music streaming from /cd/BGM.ADP
 */

#pragma once

/* Checks for BGM.ADP once and remembers the result. Call after savefile_init. */
void bgm_init(void);

/* True when a valid BGM.ADP was found at boot. Drives the settings row visibility. */
int bgm_available(void);

/* Keeps the stream fed and reacts to the Play BGM setting. Call once per frame. */
void bgm_poll(void);

/* Stops playback and releases the AICA. Must run before launching any game
 * or exiting to BIOS so the next program gets a clean sound chip. */
void bgm_shutdown(void);
