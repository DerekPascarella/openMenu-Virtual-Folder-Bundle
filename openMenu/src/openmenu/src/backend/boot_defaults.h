/*
 * File: boot_defaults.h
 * Project: backend
 * Description: Boot options from /cd/DEFAULTS.INI
 */

#pragma once

/* Reads /cd/DEFAULTS.INI once and captures its boot options. When the Honor
 * Menu Defaults setting is on, forces the style and theme it names. Call
 * after theme_manager_load and before the first ui_set_choice so the forced
 * theme can be resolved against the scanned theme lists. */
void boot_defaults_apply(void);

/* True only when [MISC] serial_sd_warning has the parsed value "1". This
 * disc option is independent of style and theme overrides. */
int boot_defaults_serial_sd_warning_enabled(void);

/* True when the disc carries a [DEFAULTS] section with a usable style.
 * Drives the Honor Menu Defaults settings row visibility. */
int boot_defaults_available(void);

/* Puts back the style and theme that were active before the override,
 * for when the user turns Honor Menu Defaults off mid session. Safe to
 * call any time, does nothing if no override was applied. */
void boot_defaults_restore(void);
