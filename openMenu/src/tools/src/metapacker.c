/*
 * File: metapack.c
 * Project: tools
 * File Created: Thursday, 17th June 2021 12:02:11 am
 * Author: Hayden Kowalchuk
 * -----
 * Copyright (c) 2021 Hayden Kowalchuk, Hayden Kowalchuk
 * License: BSD 3-clause "New" or "Revised" License, http://www.opensource.org/licenses/BSD-3-Clause
 */
#include <ctype.h>
#include <dirent.h>
#include <errno.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <sys/types.h>
#include <unistd.h>

#define strcasecmp strcasecmp

#include <backend/db_item.h>
#include <ini.h>

#include "dat_packer_interface.h"

/* Called:
./metapack FOLDER output.dat

packs the items in the folder into the output.dat
*/

#define NUM_ARGS (2)

/* Locals */
static bin_header file_header;
static bin_item_raw* bin_items;
static unsigned char* data_buf;

static inline long int
filelen(FILE* f) {
    long int end;
    fseek(f, 0, SEEK_END);
    end = ftell(f);
    fseek(f, 0, SEEK_SET);

    return end;
}

static unsigned short
meta_genre_to_enum(const char* genre) {
    if (0) {
    } else if (strcmp(genre, "Action") == 0) {
        return GENRE_ACTION;
    } else if (strcmp(genre, "Racing") == 0) {
        return GENRE_RACING;
    } else if (strcmp(genre, "Simulation") == 0) {
        return GENRE_SIMULATION;
    } else if (strcmp(genre, "Sports") == 0) {
        return GENRE_SPORTS;
    } else if (strcmp(genre, "Lightgun") == 0) {
        return GENRE_LIGHTGUN;
    } else if (strcmp(genre, "Fighting") == 0) {
        return GENRE_FIGHTING;
    } else if (strcmp(genre, "Shooter") == 0) {
        return GENRE_SHOOTER;
    } else if (strcmp(genre, "Survival") == 0) {
        return GENRE_SURVIVAL;
    } else if (strcmp(genre, "Adventure") == 0) {
        return GENRE_ADVENTURE;
    } else if (strcmp(genre, "Platformer") == 0) {
        return GENRE_PLATFORMER;
    } else if (strcmp(genre, "RPG") == 0) {
        return GENRE_RPG;
    } else if (strcmp(genre, "Shmup") == 0) {
        return GENRE_SHMUP;
    } else if (strcmp(genre, "Strategy") == 0) {
        return GENRE_STRATEGY;
    } else if (strcmp(genre, "Puzzle") == 0) {
        return GENRE_PUZZLE;
    } else if (strcmp(genre, "Arcade") == 0) {
        return GENRE_ARCADE;
    } else if (strcmp(genre, "Music") == 0) {
        return GENRE_MUSIC;
    } else if (strcmp(genre, "0") == 0) {
        return GENRE_NONE;
    } else /* default: */
    {
        printf("META: Unknown genre: %s\n", genre);
        return GENRE_NONE;
    }
}

static unsigned short
meta_accessory_to_enum(const char* accessory) {
    if (0) {
    } else if (strcmp(accessory, "JUMP") == 0) {
        return ACCESORIES_JUMP_PACK;
    } else if (strcmp(accessory, "KEY") == 0) {
        return ACCESORIES_KEYBOARD;
    } else if (strcmp(accessory, "VGA") == 0) {
        return ACCESORIES_VGA;
    } else if (strcmp(accessory, "MS") == 0) {
        return ACCESORIES_MOUSE;
    } else if (strcmp(accessory, "OLE") == 0) {
        return ACCESORIES_MARACAS;
    } else if (strcmp(accessory, "RACE") == 0) {
        return ACCESORIES_RACING_WHEEL;
    } else if (strcmp(accessory, "MIC") == 0) {
        return ACCESORIES_MICROPHONE;
    } else if (strcmp(accessory, "ARC") == 0) {
        return ACCESORIES_ARCADE_STICK;
    } else if (strcmp(accessory, "GUN") == 0) {
        return ACCESORIES_LIGHTGUN;
    } else if (strcmp(accessory, "ETH") == 0) {
        return ACCESORIES_BBA;
    } else if (strcmp(accessory, "FISH") == 0) {
        return ACCESORIES_FISHING_ROD;
    } else if (strcmp(accessory, "ASC") == 0) {
        return ACCESORIES_ASCII_PAD;
    } else if (strcmp(accessory, "CAM") == 0) {
        return ACCESORIES_DREAMEYE;
    } else if (strcmp(accessory, "MOD") == 0) {
        return ACCESORIES_MODEM;
    } else if (strcmp(accessory, "0") == 0 || strcmp(accessory, "-") == 0) {
        return ACCESORIES_NONE;
    } else /* default: */
    {
        printf("META: Unknown accessory: %s\n", accessory);
        return ACCESORIES_NONE;
    }
}

static unsigned short
meta_parse_genre(const char* genre) {
    unsigned short ret = GENRE_NONE;
    char temp[64];
    const char* delim = "+";
    memcpy(temp, genre, strlen(genre) + 1);

    char* token = strtok(temp, delim);
    const char* item = token;
    do {
        ret += meta_genre_to_enum(item);
        item = strtok(NULL, delim);
    } while (item);
    return ret;
}

static unsigned short
meta_parse_accessories(const char* accessories) {
    unsigned short ret = ACCESORIES_NONE;
    char temp[64];
    const char* delim = "+";
    memcpy(temp, accessories, strlen(accessories) + 1);

    char* token = strtok(temp, delim);
    const char* item = token;
    do {
        ret += meta_accessory_to_enum(item);
        item = strtok(NULL, delim);
    } while (item);
    return ret;
}

static int
read_meta_ini(void* user, const char* section, const char* name, const char* value) {
    /* Parsing Meta into struct */
    db_item* item = (db_item*)user;

    if (0)
        ;
#define DB_ITEM_STRI(s, n, default)                                                                                    \
    else if (strcasecmp(section, #s) == 0 && strcasecmp(name, #n) == 0) strcpy(item->n, value);
#define DB_ITEM_CHAR(s, n, default)                                                                                    \
    else if (strcasecmp(section, #s) == 0 && strcasecmp(name, #n) == 0) item->n = (unsigned char)atoi(value);
#define DB_ITEM_GENRE(s, n, default)                                                                                   \
    else if (strcasecmp(section, #s) == 0 && strcasecmp(name, #n) == 0) item->n = meta_parse_genre(value);
#define DB_ITEM_ACCESSORY(s, n, default)                                                                               \
    else if (strcasecmp(section, #s) == 0 && strcasecmp(name, #n) == 0) item->n = meta_parse_accessories(value);
#include "backend/db_item.def"

    return 1;
}

static void
meta_init_item(db_item* item) {
    memset(item->description, '\0', sizeof(item->description));
#define DB_ITEM_STRI(s, n, default)      strcpy(item->n, default);
#define DB_ITEM_CHAR(s, n, default)      item->n = default;
#define DB_ITEM_GENRE(s, n, default)     item->n = default;
#define DB_ITEM_ACCESSORY(s, n, default) item->n = default;
#include "backend/db_item.def"
}

/* buffer is where the db_item struct should be filled */
int
game_meta_read(const char* filename, void* buffer) {
    /* Always LD/cdrom */
    FILE* ini = fopen(filename, "rb");
    if (!ini) {
        printf("INI:Error opening %s!\n", filename);
        fflush(stdout);
        /*exit or something */
        return -1;
    }
    size_t ini_size = filelen(ini);
    char* ini_buffer = malloc(ini_size + 2); /* to hold newline and NUL */
    fread(ini_buffer, ini_size, 1, ini);
    fclose(ini);
    /* Forcibly terminate string with newline and NUL */
    ini_buffer[ini_size] = '\n';
    ini_buffer[ini_size + 1] = '\0';
    db_item* item = (db_item*)buffer;

    meta_init_item(item);

    if (ini_parse_string(ini_buffer, read_meta_ini, buffer) < 0) {
        printf("INI:Error Parsing %s!\n", filename);
        fflush(stdout);
        /*exit or something */
        return -1;
    }
    free(ini_buffer);

    return 0;
}

int
add_bin_file(const char* path, const char* folder, struct stat* statptr) {
    char temp_id[12];
    char temp_file[FILENAME_MAX];

    if (file_header.chunk_size == 0) {
        file_header.chunk_size = sizeof(db_item);
        data_buf = malloc(file_header.chunk_size * file_header.padding0); /* Temporarily use padding0 as num_files */
        /* work out if we need padding chunks */
        uint32_t total_header_size = sizeof(bin_header) + (file_header.padding0 * sizeof(bin_item_raw));
        /* Use padding0 for how many extra chunks may be used for header, this will add to bin_item offset */
        file_header.padding0 = total_header_size / file_header.chunk_size;
        printf("Total header chunks: %u\n\n", file_header.padding0 + 1);
    }

    /* Check if filename too long, dont try to reconcile, just skip */
    char* dot = strrchr(path, '.');
    if ((size_t)dot - (size_t)path > 11) {
        printf("Err: filename too long \"%s\", maxlength = 11!\n", path);
        return -1;
    }

    temp_file[0] = '\0';
    strcpy(temp_file, folder);
    strcat(temp_file, PATH_SEP);
    strcat(temp_file, path);
    db_item* record = (db_item*)(data_buf + (file_header.num_chunks * file_header.chunk_size));
    game_meta_read(temp_file, record);

    /* Use filename as ID, remove extension */
    memset(temp_id, '\0', sizeof(temp_id));
    strncpy(temp_id, path, 11);
    char* end = strrchr(temp_id, '.');
    if (end) {
        const size_t nul_len = sizeof(temp_id) - ((size_t)end - (size_t)temp_id);
        memset(end, '\0', nul_len);
    }
    char* temp_start = temp_id;
    while (*temp_start) {
        *temp_start = toupper(*temp_start);
        ++temp_start;
    }
    temp_id[11] = '\0';
    temp_id[10] = '\0';
    memcpy(&bin_items[file_header.num_chunks].ID, temp_id, sizeof(bin_items->ID));

    printf("id:%s\nnum_players:%d\nvmu_blocks:%d\naccessories:%d\ngenre:%d\ndesc:%s\n\n", temp_id, record->num_players,
           record->vmu_blocks, record->accessories, record->genre, record->description);

    bin_items[file_header.num_chunks].offset = file_header.padding0 + file_header.num_chunks + 1;
    (void)file_header.num_chunks++;

    printf("Added[%u] as %s\n", file_header.padding0 + file_header.num_chunks, temp_id);
    return 0;
}

int
main(int argc, char** argv) {
    if (argc < NUM_ARGS + 1 /*binary itself*/) {
        printf("Incorrect usage!\n\t./datpack FOLDER output.dat\n");
        return 1;
    }

    /* Setup file constraints */
    memcpy(&file_header.magic.rich.alpha, "DAT", 3);
    file_header.magic.rich.version = 1;
    file_header.chunk_size = 0;
    file_header.num_chunks = 0;
    file_header.padding0 = 0;

    open_output(argv[2]);
    iterate_dir(argv[1], add_bin_file, &file_header, &bin_items);
    write_bin_file(&file_header, bin_items, data_buf);

    return EXIT_SUCCESS;
}