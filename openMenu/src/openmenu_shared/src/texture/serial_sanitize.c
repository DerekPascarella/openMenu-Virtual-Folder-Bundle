/*
 * File: serial_sanitize.c
 * Project: texture
 * File Created: Tuesday, 8th June 2021 10:24:28 pm
 * Author: Hayden Kowalchuk
 * -----
 * Copyright (c) 2021 Hayden Kowalchuk, Hayden Kowalchuk
 * License: BSD 3-clause "New" or "Revised" License,
 * http://www.opensource.org/licenses/BSD-3-Clause
 */

#include <uthash.h>

/* Disc serial becomes the filename (e.g., T8119N.PVR). */
/* Name, IP Serial, Disc Serial */
/* F355 Challenge: Passione Rossa, MK-0100, T-8119N */

enum REMAP_TYPE {
    REMAP_NONE = (0 << 0), // 0
    REMAP_ART = (1 << 0),  // 1
    REMAP_META = (1 << 1), // 2
};

#define REMAP_ADD_ART(ip, disc)                                                                                        \
    (serial_remap) { .ip_serial = ip, .art_serial = disc, .remap_choice = REMAP_ART }

#define REMAP_ADD_META(ip, disc)                                                                                       \
    (serial_remap) { .ip_serial = ip, .meta_serial = disc, .remap_choice = REMAP_META }

#define REMAP_ADD_BOTH(ip, disc)                                                                                       \
    (serial_remap) { .ip_serial = ip, .meta_serial = disc, .art_serial = disc, .remap_choice = REMAP_META | REMAP_ART }

typedef struct serial_remap {
    const char* ip_serial;
    const char* art_serial;
    const char* meta_serial;
    UT_hash_handle hh; /* makes this structure hashable */
    const enum REMAP_TYPE remap_choice;
} serial_remap;

/* Generate from excel with:
find:     ^(.*)[\t](.*)[\n]
replace:  REMAP_ADD_META("$1", "$2"),\n
*/

static serial_remap serial_remap_members[] = {
    /* PAL Regional Duplicates */
    REMAP_ADD_BOTH("T13001D05", "T13001D"), /* Blue Stinger */
    REMAP_ADD_BOTH("T8111D58", "T8111D50"), /* ECW Hardcore Revolution */
    /* T9705D50 -> T9706D50  NBA Showtime: NBA on NBC, incorrect IP.BIN.
     * T7003D   -> T7005D    Plasma Sword: Nightmare of Bilstein, incorrect IP.BIN. */
    REMAP_ADD_BOTH("T45001D09", "T45001D05"), /* Tom Clancy's Rainbow Six */
    REMAP_ADD_BOTH("T45001D18", "T45001D05"), /* Tom Clancy's Rainbow Six */
    REMAP_ADD_BOTH("T45002D09", "T45002D05"), /* Tom Clancy's Rainbow Six: Rogue Spear */
    REMAP_ADD_BOTH("T36815D06", "T36804D05"), /* Tomb Raider Chronicles */
    REMAP_ADD_BOTH("T36815D13", "T36804D05"), /* Tomb Raider Chronicles */
    REMAP_ADD_BOTH("T36815D18", "T36804D05"), /* Tomb Raider Chronicles */
    REMAP_ADD_BOTH("MK5109506", "MK5109505"), /* UEFA Dream Soccer */
    REMAP_ADD_BOTH("MK5109509", "MK5109505"), /* UEFA Dream Soccer */
    REMAP_ADD_BOTH("MK5109518", "MK5109505"), /* UEFA Dream Soccer */
    REMAP_ADD_BOTH("T8103N18", "T8103N50"),   /* WWF Attitude */

    /* PAL Missing Meta */
    REMAP_ADD_META("T10001D", "T10004N"),
    REMAP_ADD_META("MK5100450", "MK51004"),
    REMAP_ADD_META("MK5117850", "MK51178"), /* NBA 2K2 */
    REMAP_ADD_META("T9713D", "T9709N"),
    REMAP_ADD_META("T9705D50", "T9706N"),
    REMAP_ADD_META("T9703D50", "T9703N"),
    REMAP_ADD_META("T8102D", "T8101N"),
    REMAP_ADD_META("MK5102550", "MK51025"),
    REMAP_ADD_META("T9502D50", "T9504N"), /* Nightmare Creatures II */
    REMAP_ADD_META("MK5110250", "MK51102"),
    REMAP_ADD_META("T7003D", "T1207N"),
    REMAP_ADD_META("T17710D50", "T17713N"),
    REMAP_ADD_META("T8106D50", "T31101N"),
    REMAP_ADD_META("MK5106150", "MK51061"),
    REMAP_ADD_META("T45006D50", "17701N"),
    REMAP_ADD_META("17701D", "17701N"),
    REMAP_ADD_META("17707D", "17707N"),
    REMAP_ADD_META("T8107D", "T8109N"),
    REMAP_ADD_META("T7012D", "T40218N"),
    REMAP_ADD_META("MK5102151", "T40215N"),
    REMAP_ADD_META("T7004D", "T1205N"),
    REMAP_ADD_META("T7021D", "T1220N"),
    REMAP_ADD_META("T22901D", "T22901N"),
    REMAP_ADD_META("T9709D50", "T9707N"),
    REMAP_ADD_META("MK5100650", "MK51006"),
    REMAP_ADD_META("MK5105350", "MK51053"),
    REMAP_ADD_META("MK5101950", "MK51019"),
    REMAP_ADD_META("T8104D", "T8106N"),
    REMAP_ADD_META("T9505D", "T9507N"),
    REMAP_ADD_META("T15109D", "T15108N"),
    REMAP_ADD_META("T15104D", "T15106N"),
    REMAP_ADD_META("T17722D", "T40207N"),
    REMAP_ADD_META("T17726D", "T40212N"),
    REMAP_ADD_META("MK5100050", "MK51000"),
    REMAP_ADD_META("MK5106050", "MK51060"),
    REMAP_ADD_META("T1401D", "T1401N"),
    REMAP_ADD_META("T41401D", "T41401N"),
    REMAP_ADD_META("T8105D50", "T8105N"),
    REMAP_ADD_META("T8112D50", "T8116N"),
    REMAP_ADD_META("MK5105150", "MK51051"),
    REMAP_ADD_META("T36816D", "T1216N"),
    REMAP_ADD_META("T45004D", "T41704N"),
    REMAP_ADD_META("T17702D", "T17702N"),
    REMAP_ADD_META("T17713D", "T17718N"),
    REMAP_ADD_META("T13011D50", "T13008N"), /* Spider-Man */
    REMAP_ADD_META("T8117D50", "T8118N"),
    REMAP_ADD_META("T23001D", "T23001N"),
    REMAP_ADD_META("T13010D", "T23003N"),
    REMAP_ADD_META("T17723D", "T40209N"),
    REMAP_ADD_META("T7005D", "T1203N"),
    REMAP_ADD_META("T7013D50", "T1213N"),
    REMAP_ADD_META("T7006D", "T1210N"),
    REMAP_ADD_META("T17711D", "T17708N"),
    REMAP_ADD_META("T40206D", "T40206N"),
    REMAP_ADD_META("T17721D", "T40216N"),
    REMAP_ADD_META("T17703D", "T17703N"),
    REMAP_ADD_META("T36807D", "T36805N"),
    REMAP_ADD_META("T36808D", "T36808N"),
    REMAP_ADD_META("T7009D50", "T1208N"),
    REMAP_ADD_META("T8108D", "T8108N"),
    REMAP_ADD_META("T9503D", "T9512N"),
    REMAP_ADD_META("MK5100250", "MK51002"),
    REMAP_ADD_META("MK5101153", "MK51011"),
    REMAP_ADD_META("T40201D", "T40202N"),
    REMAP_ADD_META("T40210D", "T40211N"),
    REMAP_ADD_META("T45001D05", "T40401N"),
    REMAP_ADD_META("T45002D05", "T40402N"),
    REMAP_ADD_META("T36815D05", "T36812N"),
    REMAP_ADD_META("T36804D05", "T36806N"),
    REMAP_ADD_META("T13008D", "T13006N"),
    REMAP_ADD_META("T40204D", "T40205N"),
    REMAP_ADD_META("MK5102050", "MK57020"),
    REMAP_ADD_META("T8101D50", "T8102N"),
    REMAP_ADD_META("T40203D", "T40204N"),
    REMAP_ADD_META("T15113D", "T15125N"),
    REMAP_ADD_META("T36810D", "T36810N"),
    REMAP_ADD_META("T8110D50", "T8110N"),
    REMAP_ADD_META("T13002D", "T13002N"),
    REMAP_ADD_META("MK5109450", "T44301N"),
    REMAP_ADD_META("MK5100150", "MK51001"),
    REMAP_ADD_META("MK5102850", "MK51028"),
    REMAP_ADD_META("MK5105450", "MK51054"),
    REMAP_ADD_META("T15106D", "T15113N"),
    REMAP_ADD_META("T36809D", "T36804N"),
    REMAP_ADD_META("T40504D", "T8111N"),
    REMAP_ADD_META("T40601D", "T40601N"),
    REMAP_ADD_META("T7016D", "T22904N"),
    REMAP_ADD_META("T8103N50", "T8103N"),
    REMAP_ADD_META("T10003D", "T10005N"),

    /* JAP Missing Meta */
    REMAP_ADD_META("HDR0054", "MK51053"), /* Sega GT */
    REMAP_ADD_META("HDR0053", "MK51035"),
    REMAP_ADD_META("HDR0159", "MK51136"),
    REMAP_ADD_META("T3601M", "T3602M"),
    REMAP_ADD_META("T3602M", "T3601N"),
    REMAP_ADD_META("T40903M", "T40901M"),
    REMAP_ADD_META("HDR0129", "MK51100"),
    REMAP_ADD_META("HDR0163", "MK51193"),
    REMAP_ADD_META("HDR0178", "MK5119250"),
    REMAP_ADD_META("HDR0010", "MK51019"),
    REMAP_ADD_META("HDR0063", "MK51092"),
    REMAP_ADD_META("HDR0016", "MK5105950"),
    REMAP_ADD_META("HDR0164", "MK5118450"),
    REMAP_ADD_META("T30801M", "T40202N"),
    REMAP_ADD_META("T30803M", "T40211N"),
    REMAP_ADD_META("HDR0029", "MK51051"),
};

static const int serials_added = sizeof(serial_remap_members) / sizeof(serial_remap);
static serial_remap* serial_remap_list = NULL;

const char*
serial_santize_art(const char* id) {
    const serial_remap* item;
    const char* ret = id;

    HASH_FIND_STR(serial_remap_list, id, item);

    if (item && (item->remap_choice & REMAP_ART)) {
        ret = item->art_serial;
    }
    return ret;
}

const char*
serial_santize_meta(const char* id) {
    const serial_remap* item;
    const char* ret = id;

    HASH_FIND_STR(serial_remap_list, id, item);

    if (item && (item->remap_choice & REMAP_META)) {
        ret = item->meta_serial;
    }
    return ret;
}

int
serial_sanitizer_init(void) {
    for (int i = 0; i < serials_added; i++) {
        HASH_ADD_STR(serial_remap_list, ip_serial, &serial_remap_members[i]);
    }

    return 0;
}
