/*
 * File: main.c
 * Project: kos_pvr_texture_load
 * File Created: Wednesday, 23rd January 2019 8:07:09 pm
 * Author: Hayden Kowalchuk (hayden@hkowsoftware.com)
 * -----
 * Copyright (c) 2019 Hayden Kowalchuk
 */
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include <arch/arch.h>
#include <arch/exec.h>
#include <dc/cdrom.h>
#include <dc/flashrom.h>
#include <dc/maple.h>
#include <dc/maple/controller.h>
#include <dc/maple/keyboard.h>
#include <dc/maple/vmu.h>
#include <dc/pvr.h>
#include <dc/video.h>
#include <kos/thread.h>

#include <backend/bgm.h>
#include <backend/boot_defaults.h>
#include <backend/db_list.h>
#include <backend/gd_list.h>
#include <openmenu_debug.h>
#include <openmenu_lcd.h>
#include <openmenu_savefile.h>
#include <openmenu_settings.h>
#include "backend/gdemu_sdk.h"
#include "ui/common.h"
#include "ui/dc/input.h"
#include "ui/dc/pvr_texture.h"
#include "ui/draw_prototypes.h"
#include "ui/ui_common.h"
#include "ui/ui_menu_credits.h"
#include "vm2/vm2_api.h"
#include "vmu_lcd_utils.h"

#if DEBUG_MAPLE_FLASH
/* Flash the screen a solid color for debugging.
 * Uses vid_clear which works before PVR init. */
static void
debug_flash(uint8_t r, uint8_t g, uint8_t b) {
    /* Pack RGB into the format vid_clear expects */
    vid_clear(r, g, b);
    thd_sleep(300); /* 300ms visible flash */
}

#define DFLASH(r, g, b) debug_flash(r, g, b)
#else
#define DFLASH(r, g, b) ((void)0)
#endif

/* show loading screen after PVR init */
static void
show_loading_screen(void) {
    uint32_t width, height, format;
    pvr_ptr_t texture;

    /* Load the loading screen texture */
    texture = load_pvr("FONT/LOADING.PVR", &width, &height, &format);
    if (!texture) {
        /* Failed to load - just skip the loading screen */
        return;
    }

    /* Calculate centered position (640x480 screen, 128x128 image) */
    const float x1 = (640.0f - (float)width) / 2.0f;
    const float y1 = (480.0f - (float)height) / 2.0f;
    const float x2 = x1 + (float)width;
    const float y2 = y1 + (float)height;
    const float z = 1.0f;

    /* Render one frame with the loading texture */
    pvr_wait_ready();
    pvr_scene_begin();

    pvr_list_begin(PVR_LIST_OP_POLY);

    /* Set up textured polygon */
    pvr_poly_cxt_t cxt;
    pvr_poly_hdr_t hdr;
    pvr_poly_cxt_txr(&cxt, PVR_LIST_OP_POLY, format, width, height, texture, PVR_FILTER_BILINEAR);
    pvr_poly_compile(&hdr, &cxt);
    pvr_prim(&hdr, sizeof(hdr));

    /* Draw quad vertices (triangle strip order) */
    pvr_vertex_t vert;

    vert.flags = PVR_CMD_VERTEX;
    vert.x = x1;
    vert.y = y1;
    vert.z = z;
    vert.u = 0.0f;
    vert.v = 0.0f;
    vert.argb = 0xFFFFFFFF;
    vert.oargb = 0;
    pvr_prim(&vert, sizeof(vert));

    vert.x = x2;
    vert.y = y1;
    vert.u = 1.0f;
    vert.v = 0.0f;
    pvr_prim(&vert, sizeof(vert));

    vert.x = x1;
    vert.y = y2;
    vert.u = 0.0f;
    vert.v = 1.0f;
    pvr_prim(&vert, sizeof(vert));

    vert.flags = PVR_CMD_VERTEX_EOL;
    vert.x = x2;
    vert.y = y2;
    vert.u = 1.0f;
    vert.v = 1.0f;
    pvr_prim(&vert, sizeof(vert));

    pvr_list_finish();
    pvr_scene_finish();

    /* Free the texture - we don't need it after this */
    pvr_mem_free(texture);
}

/* UI Collection */
#include "ui/ui_grid.h"
#undef UI_NAME
#include "ui/ui_line_desc.h"
#undef UI_NAME
#include "ui/ui_scroll.h"
#undef UI_NAME
#include "ui/ui_folders.h"
#undef UI_NAME

#include "bloader.h"
#include "texture/txr_manager.h"

/* VM2/VMUPro/USB4Maple/Pico2Maple device tracking */
#define VM2_MAX_DEVICES 8
maple_device_t* vm2_devices[VM2_MAX_DEVICES] = {NULL};
char vm2_type_names[VM2_MAX_DEVICES][16];
int vm2_device_count = 0;

void (*current_ui_init)(void);
void (*current_ui_setup)(void);
void (*current_ui_draw_OP)(void);
void (*current_ui_draw_TR)(void);
void (*current_ui_handle_input)(unsigned int);

typedef struct ui_template {
    void (*init)(void);
    void (*setup)(void);
    void (*drawOP)(void);
    void (*drawTR)(void);
    void (*handle_input)(unsigned int);
} ui_template;

#define UI_TEMPLATE(name)                                                                                              \
    (ui_template) {                                                                                                    \
        .init = FUNC_NAME(name, init), .setup = FUNC_NAME(name, setup), .drawOP = FUNC_NAME(name, drawOP),             \
        .drawTR = FUNC_NAME(name, drawTR), .handle_input = FUNC_NAME(name, handle_input),                              \
    }

static ui_template ui_choices[] = {
    UI_TEMPLATE(LIST_DESC),
    UI_TEMPLATE(GRID_3),
    UI_TEMPLATE(SCROLL),
    UI_TEMPLATE(FOLDERS),
};

static const int num_ui_choices = sizeof(ui_choices) / sizeof(ui_template);
static int need_reload_ui = 0;

static void
ui_set_choice(int choice) {
    need_reload_ui = 0;
    if (choice < UI_START || choice >= num_ui_choices) {
        choice = UI_START;
    }
    current_ui_init = ui_choices[choice].init;
    current_ui_setup = ui_choices[choice].setup;
    current_ui_draw_OP = ui_choices[choice].drawOP;
    current_ui_draw_TR = ui_choices[choice].drawTR;
    current_ui_handle_input = ui_choices[choice].handle_input;

    /* Call init & setup */
    (*current_ui_init)();
    (*current_ui_setup)();
}

void
reload_ui(void) {
    need_reload_ui = 1;
}

void
vm2_rescan(void) {
    vm2_device_count = 0;
    for (int i = 0; i < 8; i++) {
        maple_device_t* vmu = maple_enum_type(i, MAPLE_FUNC_MEMCARD);
        /* Check both non-NULL and valid to avoid sending commands to
         * stale/uninitialized device structures */
        if (vmu && vmu->valid && check_vm2_present(vmu)) {
            vm2_devices[vm2_device_count] = vmu;
            /* Cache type name from ALLINFO response still in recv_buff */
            strncpy(vm2_type_names[vm2_device_count], get_last_allinfo_type_name(), sizeof(vm2_type_names[0]) - 1);
            vm2_type_names[vm2_device_count][sizeof(vm2_type_names[0]) - 1] = '\0';
            vm2_device_count++;
        }
    }
}

const char*
vm2_get_type_name(maple_device_t* dev) {
    if (!dev) {
        return "VMU";
    }
    for (int i = 0; i < vm2_device_count; i++) {
        if (vm2_devices[i] == dev) {
            return vm2_type_names[i];
        }
    }
    return "VMU";
}

void
vm2_send_id_to_all(const char* product, const char* name) {
    if (vm2_device_count == 0) {
        return;
    }

    if (sf_vm2_send_all[0] == VM2_SEND_OFF) {
        /* User disabled game ID transmission */
        return;
    } else if (sf_vm2_send_all[0] == VM2_SEND_FIRST) {
        /* Send to first device only */
        vm2_set_id(vm2_devices[0], product, name);
    } else {
        /* Send to all detected VM2 devices (default) */
        for (int i = 0; i < vm2_device_count; i++) {
            vm2_set_id(vm2_devices[i], product, name);
        }
    }
}

/* send LCD icon on VMU hot-insert */
#define VMU_LCD_CHECK_INTERVAL 120 /* ~2 seconds at 60fps */
static bool vmu_lcd_prev_valid[8] = {false};
static int vmu_lcd_frame_counter = 0;

static void
vmu_lcd_check_insertions(void) {
    if (++vmu_lcd_frame_counter < VMU_LCD_CHECK_INTERVAL) {
        return;
    }
    vmu_lcd_frame_counter = 0;

    for (int port = 0; port < 4; port++) {
        for (int unit = 1; unit <= 2; unit++) {
            int idx = port * 2 + (unit - 1);
            maple_device_t* dev = maple_enum_dev(port, unit);
            bool now_valid = dev && dev->valid;

            if (now_valid && !vmu_lcd_prev_valid[idx]) {
                if (dev->info.functions & MAPLE_FUNC_LCD) {
                    vmu_draw_lcd_auto(dev, openmenu_lcd);
                }
            }

            vmu_lcd_prev_valid[idx] = now_valid;
        }
    }
}

static int
init() {
    int ret = 0;

    /* Load settings */
    savefile_init();

    ret += txr_create_small_pool();
    ret += txr_create_large_pool();
    ret += txr_load_DATs();
    ret += list_read_default();
    check_bloom_available(); /* Check for BLOOM.BIN once at startup */
    ret += db_load_DAT();
    ret += theme_manager_load();
    bgm_init(); /* Check for BGM.ADP once at startup */

    /* Force the disc's default style/theme if configured and honored.
     * Needs the theme scan above so names resolve to indices. */
    boot_defaults_apply();

    /* Initialize folder tree after loading game list */
    list_folder_init();

    if (!sf_filter[0]) {
        switch (sf_sort[0]) {
            case SORT_NAME: list_set_sort_name(); break;

            case SORT_DATE: list_set_sort_region(); break;

            case SORT_PRODUCT: list_set_sort_genre(); break;

            case SORT_SD_CARD: list_set_sort_default(); break;

            default:
            case SORT_DEFAULT: list_set_sort_alphabetical(); break;
        }
    } else {
        list_set_genre_sort((FLAGS_GENRE)sf_filter[0] - 1, sf_sort[0]);
    }

    /* setup internal memory zones */
    draw_init();

    /* Load UI */
    ui_set_choice(sf_ui[0]);

    return ret;
}

static void
draw(void) {
    pvr_wait_ready();
    pvr_scene_begin();

    draw_set_list(PVR_LIST_OP_POLY);
    pvr_list_begin(PVR_LIST_OP_POLY);

    (*current_ui_draw_OP)();

    pvr_list_finish();

    draw_set_list(PVR_LIST_TR_POLY);
    pvr_list_begin(PVR_LIST_TR_POLY);

    (*current_ui_draw_TR)();

    pvr_list_finish();

    pvr_scene_finish();
}

static void
processInput(void) {
    inputs _input;
    unsigned int buttons;

    maple_device_t* cont;
    maple_device_t* kbd;
    cont_state_t* state;
    kbd_state_t* kbd_state;

    /*  Reset Everything */
    memset(&_input, 0, sizeof(inputs));

    /* Set neutral analog values */
    _input.axes_1 = 128; /* Neutral analog X */
    _input.axes_2 = 128; /* Neutral analog Y */

    cont = maple_enum_type(0, MAPLE_FUNC_CONTROLLER);
    kbd = maple_enum_type(0, MAPLE_FUNC_KEYBOARD);

    if (!cont && !kbd) {
        /* No controller or keyboard - send neutral input */
        INPT_ReceiveFromHost(_input);
        return;
    }

    if (cont && cont->valid) {
        /* Controller found - also works with the controller portion of a light gun */
        state = (cont_state_t*)maple_dev_status(cont);
        buttons = state->buttons;

        /* DPAD */
        _input.dpad = (state->buttons >> 4) & ~240; // mrneo240 ;)

        /* BUTTONS */
        _input.btn_a = (uint8_t)!!(buttons & CONT_A);
        _input.btn_b = (uint8_t)!!(buttons & CONT_B);
        _input.btn_x = (uint8_t)!!(buttons & CONT_X);
        _input.btn_y = (uint8_t)!!(buttons & CONT_Y);
        _input.btn_start = (uint8_t)!!(buttons & CONT_START);

        /* ANALOG */
        _input.axes_1 = ((uint8_t)(state->joyx) + 128);
        _input.axes_2 = ((uint8_t)(state->joyy) + 128);

        /* TRIGGERS */
        if (!strncmp("Dreamcast Fishing Controller", cont->info.product_name, 28)) {
            _input.trg_left = 0;
            _input.trg_right = 0;
        } else {
            _input.trg_left = (uint8_t)state->ltrig & 255;
            _input.trg_right = (uint8_t)state->rtrig & 255;
        }
    }

    if (kbd && kbd->valid) {
        /* Keyboard found - copy list of pressed key scancodes from cond.keys */
        kbd_state = (kbd_state_t*)maple_dev_status(kbd);
        _input.kbd_modifiers = kbd_state->shift_keys;
        for (int i = 0; i < INPT_MAX_KEYBOARD_KEYS; i++) {
            _input.kbd_buttons[i] = kbd_state->cond.keys[i];
        }
    }

    INPT_ReceiveFromHost(_input);
}

static int
translate_input(void) {
    processInput();

    /* D-Pad directions */
    if (INPT_DPADDirection(DPAD_LEFT)) {
        return LEFT;
    }
    if (INPT_DPADDirection(DPAD_RIGHT)) {
        return RIGHT;
    }
    if (INPT_DPADDirection(DPAD_UP)) {
        return UP;
    }
    if (INPT_DPADDirection(DPAD_DOWN)) {
        return DOWN;
    }

    /* Analog stick */
    if (INPT_AnalogI(AXES_X) < 128 - 24) {
        return LEFT;
    }
    if (INPT_AnalogI(AXES_X) > 128 + 24) {
        return RIGHT;
    }

    if (INPT_AnalogI(AXES_Y) < 128 - 24) {
        return UP;
    }
    if (INPT_AnalogI(AXES_Y) > 128 + 24) {
        return DOWN;
    }

    /* Buttons - use edge detection (BTN_PRESS) for A, B, Y, START to prevent
     * double-press bugs when transitioning between UI states. X keeps hold
     * detection for the artwork zoom feature. */
    if (INPT_ButtonEx(BTN_A, BTN_PRESS)) {
        return A;
    }
    if (INPT_ButtonEx(BTN_B, BTN_PRESS)) {
        return B;
    }
    if (INPT_Button(BTN_X)) {
        return X;
    }
    if (INPT_ButtonEx(BTN_Y, BTN_PRESS)) {
        return Y;
    }
    if (INPT_ButtonEx(BTN_START, BTN_PRESS)) {
        return START;
    }

    /* Triggers */
    if (INPT_TriggerPressed(TRIGGER_L)) {
        return TRIG_L;
    }
    if (INPT_TriggerPressed(TRIGGER_R)) {
        return TRIG_R;
    }

    /* Keyboard support - skip if no keys pressed */
    if (!INPT_KeyboardNone()) {
        /* Check if Shift is held, skip letter/number button mappings
         * so UI quick-jump (Shift+Letter/Number) works without also
         * triggering the action mapped to that key */
        uint8_t mods = INPT_KeyboardModifiers();
        bool shift_held = (mods & KBD_MOD_LSHIFT) || (mods & KBD_MOD_RSHIFT);

        /* Arrow keys → D-Pad (always active, even with Shift) */
        if (INPT_KeyboardButton(KBD_KEY_LEFT)) {
            return LEFT;
        }
        if (INPT_KeyboardButton(KBD_KEY_RIGHT)) {
            return RIGHT;
        }
        if (INPT_KeyboardButton(KBD_KEY_UP)) {
            return UP;
        }
        if (INPT_KeyboardButton(KBD_KEY_DOWN)) {
            return DOWN;
        }

        if (!shift_held) {
            /* Letter keys → button mappings (disabled when Shift held for quick-jump) */
            if (INPT_KeyboardButtonPress(KBD_KEY_Z)) {
                return A;
            }
            if (INPT_KeyboardButtonPress(KBD_KEY_X)) {
                return B;
            }
            if (INPT_KeyboardButton(KBD_KEY_A)) {
                return X;
            }
            if (INPT_KeyboardButtonPress(KBD_KEY_S)) {
                return Y;
            }
            if (INPT_KeyboardButton(KBD_KEY_Q)) {
                return TRIG_L;
            }
            if (INPT_KeyboardButton(KBD_KEY_W)) {
                return TRIG_R;
            }
        }

        /* Non-letter keys → button mappings (always active, not quick-jump targets) */
        if (INPT_KeyboardButtonPress(KBD_KEY_SPACE)) {
            return A;
        }
        if (INPT_KeyboardButtonPress(KBD_KEY_ESCAPE)) {
            return B;
        }
        if (INPT_KeyboardButtonPress(KBD_KEY_ENTER)) {
            return START;
        }
        if (INPT_KeyboardButton(KBD_KEY_PGUP)) {
            return TRIG_L;
        }
        if (INPT_KeyboardButton(KBD_KEY_PGDOWN)) {
            return TRIG_R;
        }
    }

    return NONE;
}

static void
init_gfx_pvr(void) {
    /* BlueCrab (c) 2014,
    This assumes that the video mode is initialized as KOS
   normally does, that is to 640x480 NTSC IL or 640x480 VGA */
    int dc_region, ct;

    dc_region = flashrom_get_region();
    ct = vid_check_cable();

    /* Prompt the user for whether to run in PAL50 or PAL60 if the flashrom says
       the Dreamcast is European and a VGA Box is not hooked up. */
    if (dc_region == FLASHROM_REGION_EUROPE && ct != CT_VGA) {
        if (/*pal_menu()*/ 1 == 1) {
            vid_set_mode(DM_640x480_NTSC_IL, PM_RGB565);
        } else {
            vid_set_mode(DM_640x480_PAL_IL, PM_RGB565);
        }
    }

    pvr_init_params_t params = {
        /* Enable opaque and translucent polygons with size 32 and 32 */
        {PVR_BINSIZE_32, PVR_BINSIZE_0, PVR_BINSIZE_32, PVR_BINSIZE_0, PVR_BINSIZE_0}, /* Only TR */
        256 * 1024,                                                                    /* 256kb Vertex buffer  */
        0,                                                                             /* No DMA, but maybe? */
        0,                                                                             /* No FSAA */
        0,                                                                             /* Disable TR autosort */
        0};

    pvr_init(&params);
    draw_set_list(PVR_LIST_OP_POLY);
}

int
main(int argc, char* argv[]) {
    /* unused */
    (void)argc;
    (void)argv;

    // gdemu_set_img_num(1);
    // thd_sleep(500);

    /* DEBUG: RED = before maple_wait_scan */
    DFLASH(255, 0, 0);

    /* wait for maple bus scan */
    maple_wait_scan();

    /* DEBUG: GREEN = after maple_wait_scan */
    DFLASH(0, 255, 0);

    /* DEBUG: BLUE = before vm2_rescan */
    DFLASH(0, 0, 255);

    /* Scan for VM2/VMUPro/USB4Maple/Pico2Maple devices and send initial ID */
    vm2_rescan();

    /* DEBUG: YELLOW = after vm2_rescan */
    DFLASH(255, 255, 0);

    for (int i = 0; i < vm2_device_count; i++) {
        maple_device_t* vmu = vm2_devices[i];
        int port = vmu->port;
        int unit = vmu->unit;

        vm2_set_id(vmu, "openmenu", NULL);
        thd_sleep(200);

        while (!maple_enum_dev(port, unit)) {
            thd_pass();
        }
    }

    /* fflush(stdout); */
    /* setbuf(stdout, NULL); */

    /* DEBUG: CYAN = before init_gfx_pvr */
    DFLASH(0, 255, 255);

    init_gfx_pvr();

    /* Show loading screen while init() runs (which includes a 1s delay) */
    show_loading_screen();

    /* DEBUG: MAGENTA = before init/savefile_init */
    DFLASH(255, 0, 255);

    if (init()) {
        /* puts("Init error."); */
        savefile_close();
        return 1;
    }

    /* DEBUG: WHITE = init complete, entering main loop */
    DFLASH(255, 255, 255);

    for (;;) {
        z_reset();
        (*current_ui_handle_input)(translate_input());
        vmu_lcd_check_insertions();
        bgm_poll();
        vid_waitvbl();
        if (need_reload_ui) {
            ui_set_choice(sf_ui[0]);
        } else {
            draw();
        }
    }

    savefile_close();
    return 0;
}

void
exit_to_bios_ex(int do_mount, int do_send_id) {
    bgm_shutdown(); /* BIOS expects a quiet AICA */
    const gd_item* item = get_cur_game_item();
    /* Only mount/set ID if we have a valid item and it's not a folder */
    /* Folders have disc="DIR" and product[0]='F' */
    if (item && strncmp(item->disc, "DIR", 3) != 0 && item->product[0] != 'F') {
        if (do_mount) {
            /* Mount the disc image */
            gdemu_set_img_num((uint16_t)item->slot_num);

            /* Wait for disc to be ready */
            extern void wait_cd_ready(gd_item * disc);
            wait_cd_ready((gd_item*)item);
        }

        /* Send game ID to VM2/VMUPro/USB4Maple/Pico2Maple if present */
        if (do_send_id) {
            vm2_rescan(); /* Rescan to detect hot-swapped devices */
            vm2_send_id_to_all(item->product, item->name);
        }
    }

    if (sf_bios_3d[0] == BIOS_3D_STANDARD) {
        arch_menu();
    }

    /* Alternate or Alternate + 3D: use bloader */
    bloader_cfg_t* bloader_config = (bloader_cfg_t*)&bloader_data[bloader_size - sizeof(bloader_cfg_t)];
    bloader_config->enable_wide = 0;

    maple_device_t* cont = maple_enum_type(0, MAPLE_FUNC_CONTROLLER);
    if (cont && !strncmp("Dreamcast Fishing Controller", cont->info.product_name, 28)) {
        bloader_config->enable_3d = 0;
    } else {
        bloader_config->enable_3d = (sf_bios_3d[0] == BIOS_3D_ALTERNATE_3D) ? 1 : 0;
    }

    arch_exec_at(bloader_data, bloader_size, 0xacf00000);
}

void
exit_to_bios(void) {
    /* Default behavior: mount disc and send ID */
    exit_to_bios_ex(1, 1);
}
