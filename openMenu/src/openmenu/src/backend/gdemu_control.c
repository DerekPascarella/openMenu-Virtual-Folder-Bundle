#include <arch/arch.h>
#include <dc/sound/sound.h>
#include <kos.h>
#include <kos/thread.h>

#include <backend/gd_item.h>
#include <openmenu_settings.h>
#include "backend/bgm.h"
#include "backend/cb_loader.h"
#include "backend/controls.p1.h"
#include "backend/gdemu_sdk.h"
#include "backend/gdmenu_binary.h"
#include "vm2/vm2_api.h"

extern int vm2_device_count;
extern void vm2_rescan(void);
extern void vm2_send_id_to_all(const char* product, const char* name);

/* BLOOM.BIN availability flag - checked once at startup */
static int bloom_available = 0;

void
check_bloom_available(void) {
    file_t fd = fs_open("/cd/BLOOM.BIN", O_RDONLY);
    if (fd != -1) {
        fs_close(fd);
        bloom_available = 1;
    } else {
        bloom_available = 0;
    }
}

int
is_bloom_available(void) {
    return bloom_available;
}

void
wait_cd_ready(gd_item* disc) {
    /* For non-game content (audio CDs, etc.), use minimal delay
     * since cdrom_reinit() will never succeed anyway */
    if (disc && !strcmp(disc->type, "other")) {
        thd_sleep(100); /* Just 100ms to let GDEMU switch images */
        return;
    }

    /* For games, use full timeout (current behavior) */
    for (int i = 0; i < 500; i++) {
        if (cdrom_reinit() == ERR_OK) {
            return;
        }
        thd_sleep(20);
    }
}

void
bloom_launch(gd_item* disc) {
    file_t fd;
    uint32_t bloom_size;
    uint8_t* bloom_buf;

    fd = fs_open("/cd/BLOOM.BIN", O_RDONLY);

    if (fd == -1) {
        /* printf("Can't open %s\n", "/cd/BLOOM.BIN"); */
        return;
    }

    /* Committed to launching, hand it a quiet AICA */
    bgm_shutdown();

    fs_seek(fd, 0, SEEK_END);
    bloom_size = fs_tell(fd);
    fs_seek(fd, 0, SEEK_SET);
    bloom_buf = (uint8_t*)malloc(bloom_size + 32);
    bloom_buf = (uint8_t*)(((uint32_t)bloom_buf & 0xffffffe0) + 0x20);
    fs_read(fd, bloom_buf, bloom_size);
    fs_close(fd);

    gdemu_set_img_num((uint16_t)disc->slot_num);

    wait_cd_ready(disc);

    /* Patch */
    ((uint16_t*)0xAC000198)[0] = 0xFF86;

    arch_exec(bloom_buf, bloom_size);
}

void
bleem_launch(gd_item* disc) {
    file_t fd;
    uint32_t bleem_size;
    uint8_t* bleem_buf;

    fd = fs_open("/cd/BLEEM.BIN", O_RDONLY);

    if (fd == -1) {
        /* printf("Can't open %s\n", "/cd/BLEEM.BIN"); */
        return;
    }

    /* Committed to launching, hand it a quiet AICA */
    bgm_shutdown();

    fs_seek(fd, 0, SEEK_END);
    bleem_size = fs_tell(fd);
    fs_seek(fd, 0, SEEK_SET);
    bleem_buf = (uint8_t*)malloc(bleem_size + 32);
    bleem_buf = (uint8_t*)(((uint32_t)bleem_buf & 0xffffffe0) + 0x20);
    fs_read(fd, bleem_buf, bleem_size);
    fs_close(fd);

    gdemu_set_img_num((uint16_t)disc->slot_num);

    wait_cd_ready(disc);

    /* Patch */
    ((uint16_t*)0xAC000198)[0] = 0xFF86;

    for (int i = 0; i < altctrl_size; i++) {
        bleem_buf[i + 0x7079C] = altctrl_data[i];
    }

    bleem_buf[0x49E6] = 0x06; // patch  restart emu A+B+X+Y+dpad down
    bleem_buf[0x49E7] = 0x0E; //       exit to menu A+B+X+Y+START

    bleem_buf[0x1CA70] = 1;

    arch_exec(bleem_buf, bleem_size);
}

void
dreamcast_launch_disc(gd_item* disc) {
    /* Hand the next program a quiet AICA */
    bgm_shutdown();

    /* For non-game discs (audio CDs, etc.), just mount and exit to BIOS */
    if (!strcmp(disc->type, "other")) {
        gdemu_set_img_num((uint16_t)disc->slot_num);
        wait_cd_ready(disc);

        /* Configure BIOS loader settings before exiting */
        extern uint8_t bloader_data[];
        extern uint32_t bloader_size;

        typedef struct {
            uint8_t enable_wide;
            uint8_t enable_3d;
        } bloader_cfg_t;

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

        /* Exit to BIOS (don't send VM2 ID for non-game discs) */
        arch_exec_at(bloader_data, bloader_size, 0xacf00000);
        return;
    }

    /* Normal game launch sequence */
    ldr_params_t param;
    param.region_free = 1;
    param.force_vga = 1;
    param.IGR = 1;
    param.boot_intro = (sf_boot_mode[0] == BOOT_MODE_FULL || sf_boot_mode[0] == BOOT_MODE_ANIMATION) ? 1 : 0;
    param.sega_license = (sf_boot_mode[0] == BOOT_MODE_FULL || sf_boot_mode[0] == BOOT_MODE_LICENSE) ? 1 : 0;

    if (!strncmp(disc->region, "JUE", 3)) {
        param.game_region = (int)(((uint8_t*)0x8C000072)[0] & 7);
    } else {
        switch (disc->region[0]) {
            case 'J': param.game_region = 0; break;
            case 'U': param.game_region = 1; break;
            case 'E': param.game_region = 2; break;
            default: param.game_region = (int)(((uint8_t*)0x8C000072)[0] & 7); break;
        }
    }

    gdemu_set_img_num((uint16_t)disc->slot_num);
    // thd_sleep(500);

    /* Only send game ID to VM2/VMU devices for actual games */
    if (strcmp(disc->type, "other") != 0) {
        vm2_rescan(); /* Rescan to detect hot-swapped devices */
        vm2_send_id_to_all(disc->product, disc->name);
    }

    wait_cd_ready(disc);

    int status = 0, disc_type = 0;

    cdrom_get_status(&status, &disc_type);

    param.disc_type = disc_type == CD_GDROM;

    if (!strncmp(disc->name, "PSO VER.2", 9) || !strncmp(disc->name, "SONIC ADVENTURE 2", 18)) {
        param.need_game_fix = 1;
    } else {
        param.need_game_fix = 0;
    }

    if (!strncmp((char*)0x8c0007CC, "1.004", 5)) {
        ((uint32_t*)0xAC000E20)[0] = 0;
    } else if (!strncmp((char*)0x8c0007CC, "1.01d", 5) || !strncmp((char*)0x8c0007CC, "1.01c", 5)) {
        ((uint32_t*)0xAC000E1C)[0] = 0;
    }

    ((uint32_t*)0xAC0000E4)[0] = -3;

    memcpy((void*)0xACCFFF00, &param, 32);

    arch_exec(gdmenu_loader, gdmenu_loader_length);
}

void
dreamcast_launch_cb(gd_item* disc) {
    file_t fd;
    uint32_t cb_size;
    uint8_t* cb_buf;
    uint32_t cheat_size = 0;
    uint8_t* cheat_buf;
    char cheat_name[32];

    fd = fs_open("/cd/PELICAN.BIN", O_RDONLY);

    if (fd == -1) {
        return;
    }

    /* Committed to launching, hand it a quiet AICA */
    bgm_shutdown();

    fs_seek(fd, 0, SEEK_END);
    cb_size = fs_tell(fd);
    fs_seek(fd, 0, SEEK_SET);
    cb_buf = (uint8_t*)malloc(cb_size + 32);
    cb_buf = (uint8_t*)(((uint32_t)cb_buf & 0xffffffe0) + 0x20);
    fs_read(fd, cb_buf, cb_size);
    fs_close(fd);

    sprintf(cheat_name, "/cd/cheats/%s.bin", disc->product);

    if ((fd = fs_open(cheat_name, O_RDONLY)) == -1) {
        fd = fs_open("/cd/cheats/FCDCHEATS.BIN", O_RDONLY);
    }

    if (fd != -1) {
        fs_seek(fd, 0, SEEK_END);
        cheat_size = fs_tell(fd);
        fs_seek(fd, 0, SEEK_SET);

        cheat_buf = (uint8_t*)malloc(cheat_size + 32);
        cheat_buf = (uint8_t*)(((uint32_t)cheat_buf & 0xffffffe0) + 0x20);

        fs_read(fd, cheat_buf, 16);

        if (!strncmp((const char*)cheat_buf, "XploderDC Cheats", 16)) {
            fs_seek(fd, 640, SEEK_SET);
            cheat_size -= 640;
            fs_read(fd, cheat_buf, cheat_size);

            if (!((uint32_t*)cheat_buf)[0]) {
                cheat_size = 0;
            }
        } else {
            cheat_size = 0;
        }

        fs_close(fd);
    }

    gdemu_set_img_num((uint16_t)disc->slot_num);
    // thd_sleep(500);

    /* Only send game ID to VM2/VMU devices for actual games */
    if (strcmp(disc->type, "other") != 0) {
        vm2_rescan(); /* Rescan to detect hot-swapped devices */
        vm2_send_id_to_all(disc->product, disc->name);
    }

    wait_cd_ready(disc);

    ((uint16_t*)0xAC000198)[0] = 0xFF86;

    int status = 0, disc_type = 0;

    cdrom_get_status(&status, &disc_type);

    if (cheat_size) {
        uint16_t* pelican = (uint16_t*)cb_buf;

        pelican[128] = 0;
        pelican[129] = 0x90;

        pelican[10818] = (uint16_t)cheat_size;
        pelican[10819] = (cheat_size >> 16);

        pelican[10820] = 0;
        pelican[10821] = 0x8CD0;

        memcpy((void*)0xACD00000, cheat_buf, cheat_size);
    }

    if (disc_type != CD_GDROM) {
        CDROM_TOC toc;
        cdrom_read_toc(&toc, 0);
        uint32_t lba = cdrom_locate_data_track(&toc);

        uint16_t* pelican = (uint16_t*)cb_buf;

        pelican[4067] = 0x711F;
        pelican[4074] = 0xE500;
        pelican[4302] = (uint16_t)lba;
        pelican[4303] = (uint16_t)(lba >> 16);
        pelican[472] = 0x0009;
        pelican[4743] = 0x0018;
        pelican[4745] = 0x0018;
        pelican[5261] = 0x0008;
        pelican[5433] = 0x0009;
        pelican[5436] = 0x0009;
        pelican[5438] = 0x0008;
        pelican[5460] = 0x0009;
        pelican[5472] = 0x0009;
        pelican[5511] = 0x0008;
        pelican[310573] = 0x64C3;
        pelican[310648] = 0x0009;
        pelican[310666] = 0x0009;
        pelican[310708] = 0x0018;
        pelican[310784] = 0x0000;
        pelican[310785] = 0x8CE1;

        memcpy((void*)0xACE10000, cb_loader_data, cb_loader_size);
    }

    arch_exec(cb_buf, cb_size);
}
