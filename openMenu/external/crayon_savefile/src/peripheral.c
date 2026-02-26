#include "crayon_savefile/peripheral.h"

vec2_s8_t
crayon_peripheral_dreamcast_get_port_and_slot(int8_t save_device_id) {
    vec2_s8_t values = {-1, -1};
    if (save_device_id < 0 || save_device_id >= 8) {
        return values;
    }

    if (save_device_id % 2 == 0) {
        values.y = 1;
    } else {
        values.y = 2;
    }

    values.x = save_device_id / 2;

    return values;
}

uint8_t
crayon_peripheral_dreamcast_get_screens() {
#if defined(_arch_dreamcast)

    uint8_t screens = 0; // a1a2b1b2c1c2d1d2

    int i;
    for (i = 0; i < 8; i++) { // 8 because we can have 8 VMUs max
        // Check if device contains this function bitmap
        if (crayon_peripheral_has_function(MAPLE_FUNC_LCD, i)) {
            screens |= (1 << i);
        }
    }
    return screens;

#else

    return 0;

#endif
}

void
crayon_peripheral_vmu_display_icon(uint8_t vmu_bitmap, void* icon) {
#if defined(_arch_dreamcast)

    maple_device_t* vmu;
    uint8_t i, j;
    for (j = 0; j <= 3; j++) {
        for (i = 1; i <= 2; i++) {
            // a1a2b1b2c1c2d1d2
            if ((vmu_bitmap >> ((2 * j) + (i - 1))) & 1) { // We want to display on this VMU
                if (!(vmu = maple_enum_dev(j, i)) || !vmu->valid) { // Device not present or invalid
                    continue;
                }
                maple_device_t* ctrl = maple_enum_dev(j, 0);
                if (ctrl && ctrl->valid && (ctrl->info.functions & MAPLE_FUNC_LIGHTGUN)) {
                    unsigned char rotated[192];
                    for (int r = 0; r < 192; r++) {
                        unsigned char b = ((const unsigned char*)icon)[191 - r];
                        b = (b & 0xF0) >> 4 | (b & 0x0F) << 4;
                        b = (b & 0xCC) >> 2 | (b & 0x33) << 2;
                        b = (b & 0xAA) >> 1 | (b & 0x55) << 1;
                        rotated[r] = b;
                    }
                    vmu_draw_lcd(vmu, rotated);
                } else {
                    vmu_draw_lcd(vmu, icon);
                }
            }
        }
    }

#endif

    return;
}

// Returns true if device has certain function/s
uint8_t
crayon_peripheral_has_function(uint32_t function, int8_t save_device_id) {
#if defined(_arch_dreamcast)

    maple_device_t* vmu;

    vec2_s8_t port_and_slot = crayon_peripheral_dreamcast_get_port_and_slot(save_device_id);

    // Invalid controller/port
    if (port_and_slot.x < 0) {
        return 0;
    }

    // Make sure there's a device in the port/slot
    if (!(vmu = maple_enum_dev(port_and_slot.x, port_and_slot.y))) {
        return 0;
    }

    // Check the device is valid and it has a certain function
    if (!vmu->valid || !(vmu->info.functions & function)) {
        return 0;
    }

#endif

    return 1;
}
