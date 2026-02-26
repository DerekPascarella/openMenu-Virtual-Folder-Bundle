/*
 * File: reader.c
 * Project: dat_builder
 * File Created: Tuesday, 8th June 2021 2:47:35 pm
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

#include <uthash.h>

#include <backend/dat_format.h>
#include <backend/gd_item.h>
#include <backend/gd_list.h>

/* Called:
./datstrip input.dat openmenu.ini output.dat

Reads an input DAT and a menu ini to then generate an optimized DAT
*/

#define NUM_ARGS (3)

/* Define configure constants */
/* only defined when building the binary tool */
#ifndef STANDALONE_BINARY
#define STANDALONE_BINARY (1)
#endif
#ifdef STANDALONE_BINARY
#define DEBUG
#endif

#ifdef DEBUG
#define DBG_PRINT(...) printf(__VA_ARGS__)
#else
#define DBG_PRINT(...)
#endif

#if defined(WIN32) || defined(WINNT)
#define PATH_SEP "\\"
#else
#define PATH_SEP "/"
#endif

typedef struct bin_item_raw {
    char ID[12];
    uint32_t offset;
} bin_item_raw;

/* DAT Writing */

/* Locals */
static bin_header file_header;
static FILE* out_fd;
static bin_item_raw* bin_items;
static unsigned char* data_buf;

void
open_output(const char* path) {
    out_fd = fopen(path, "wb");
    if (!out_fd) {
        printf("ERR: unable to open %s for writing!\n", path);
    }
}

void
write_bin_file(void) {
    char* nul =
        calloc(1, file_header.chunk_size - sizeof(file_header) - (sizeof(bin_item_raw) * file_header.num_chunks));

    printf("Writing:");
    /* Write header */
    printf("header..");
    fwrite(&file_header, sizeof(file_header), 1, out_fd);
    /* Write file list */
    printf("item list..");
    fwrite(bin_items, sizeof(bin_item_raw), file_header.num_chunks, out_fd);
    /* Write padding out to first chunk offset */
    printf("padding..");
    fwrite(nul, file_header.chunk_size - sizeof(file_header) - (sizeof(bin_item_raw) * file_header.num_chunks), 1,
           out_fd);
    /* Write out all chunks */
    printf("chunks..");
    fwrite(data_buf, file_header.num_chunks * file_header.chunk_size, 1, out_fd);

    fclose(out_fd);
    printf("done!\n");
}

int
add_bin_file(const char* path, const char* folder, struct stat* statptr) {
    char temp_id[12];
    char temp_file[FILENAME_MAX];

    if (file_header.chunk_size == 0) {
        file_header.chunk_size = (uint32_t)statptr->st_size;
        data_buf = malloc(file_header.chunk_size * file_header.padding0); /* Temporarily use padding0 as num_files */
    } else {
        if (statptr->st_size != file_header.chunk_size) {
            printf("Err: Filesize mismatch for %s, found %lld vs %u!\n", path, statptr->st_size,
                   file_header.chunk_size);
            return -1;
        }
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
    FILE* temp_fd = fopen(temp_file, "rb");
    if (!temp_fd) {
        printf("ERR: cant read %s\n", temp_file);
        return -1;
    }
    fread(data_buf + (file_header.num_chunks * file_header.chunk_size), file_header.chunk_size, 1, temp_fd);
    fclose(temp_fd);

    /* Use filename as ID, remove extension */
    memcpy(temp_id, path, 11);
    char* end = strrchr(temp_id, '.');
    if (end) {
        memset(end, '\0', sizeof(temp_id) - ((size_t)end - (size_t)temp_id));
    }
    char* temp_start = temp_id;
    while (*temp_start) {
        *temp_start = toupper(*temp_start);
        ++temp_start;
    }
    memcpy(&bin_items[file_header.num_chunks].ID, temp_id, sizeof(bin_items->ID));

    bin_items[file_header.num_chunks].offset = file_header.num_chunks + 1;
    (void)file_header.num_chunks++;

    printf("Added[%u] as %s\n", file_header.num_chunks, temp_id);
    return 0;
}

int
print_cb(const char* path, const char* folder, struct stat* statptr) {
    printf("%s\n", path);
    return 0;
}

int
iterate_dir(const char* path, int (*file_cb)(const char*, const char*, struct stat*)) {
    struct dirent* dp;
    struct stat statbuf;
    char pathbuf[FILENAME_MAX];
    uint32_t num_files_found;

    DIR* dir = opendir(path);

    if (file_cb == NULL) {
        file_cb = print_cb;
    }

    // Unable to open directory stream
    if (!dir) {
        return -1;
    }

    /* Loop twice, first time to count then second time to add */
    num_files_found = 0;
    while ((dp = readdir(dir)) != NULL) {
        /* ignore these */
        if (strcmp(dp->d_name, ".") == 0 || strcmp(dp->d_name, "..") == 0) {
            continue;
        }

        /* create proper full path */
        getcwd(pathbuf, FILENAME_MAX);
        strcat(pathbuf, PATH_SEP);
        strcat(pathbuf, path);
        strcat(pathbuf, PATH_SEP);
        strcat(pathbuf, dp->d_name);
        if (stat(pathbuf, &statbuf) == -1) {
            printf("ERR: errno = %d\n", errno);
            return -1;
        }

        /* only check files */
        if (S_ISREG(statbuf.st_mode)) {
            num_files_found++;
        }
    }
    rewinddir(dir);

    file_header.padding0 = num_files_found;
    bin_items = malloc(sizeof(bin_item_raw) * num_files_found);

    while ((dp = readdir(dir)) != NULL) {
        /* ignore these */
        if (strcmp(dp->d_name, ".") == 0 || strcmp(dp->d_name, "..") == 0) {
            continue;
        }

        /* create proper full path */
        getcwd(pathbuf, FILENAME_MAX);
        strcat(pathbuf, PATH_SEP);
        strcat(pathbuf, path);
        strcat(pathbuf, PATH_SEP);
        strcat(pathbuf, dp->d_name);
        if (stat(pathbuf, &statbuf) == -1) {
            printf("ERR: errno = %d\n", errno);
            return -1;
        }

        /* only check files */
        if (S_ISREG(statbuf.st_mode)) {
            (*file_cb)(dp->d_name, path, &statbuf);
        }
    }

    closedir(dir);
    return 0;
}

/* DAT loading */

int
DAT_init(dat_file* bin) {
    memset(bin, '\0', sizeof(dat_file));
    return 0;
}

int
DAT_load_parse(dat_file* bin, const char* path) {
    FILE* bin_fd;
    bin_header file_header;

    bin_fd = fopen(path, "rb");
    if (!bin_fd) {
        DBG_PRINT("DAT:Error opening %s!\n", path);
        return 1;
    }

    fread(&file_header, sizeof(bin_header), 1, bin_fd);
    if (file_header.magic.rich.version != 1) {
        DBG_PRINT("DAT:Error Incorrect file format!\n");
        return 1;
    }

    /* setup basic bin file info */
    bin->chunk_size = file_header.chunk_size;
    bin->num_chunks = file_header.num_chunks;
    bin->handle = bin_fd;
    bin->items = malloc(bin->num_chunks * sizeof(bin_item));
    bin->hash = NULL;

    /* Parse file table to Hash table */
    for (int i = 0; i < file_header.num_chunks; i++) {
        fread(&bin->items[i], sizeof(bin_item_raw), 1, bin->handle);
        HASH_ADD_STR(bin->hash, ID, &bin->items[i]);
    }

    printf("DAT:Parse success (%u items)!\n", file_header.num_chunks);

    return 0;
}

void
DAT_info(const dat_file* bin) {
    DBG_PRINT("BIN Stats:\nChunk Size: %u\nNum Chunks: %u\n\n", bin->chunk_size, bin->num_chunks);
    for (int i = 0; i < bin->num_chunks; i++) {
        DBG_PRINT("Record[%u] %s at 0x%X\n", bin->items[i].offset, bin->items[i].ID,
                  bin->items[i].offset * bin->chunk_size);
    }
}

uint32_t
DAT_get_offset_by_ID(const dat_file* bin, const char* ID) {
    const bin_item* item;
    uint32_t ret;

    HASH_FIND_STR(bin->hash, ID, item);
    if (item) {
        ret = item->offset * bin->chunk_size;
    } else {
        ret = 0;
    }

    return ret;
}

int
DAT_read_file_by_ID(const dat_file* bin, const char* ID, void* buf) {
    uint32_t offset = DAT_get_offset_by_ID(bin, ID);
    if (offset) {
        fseek(bin->handle, offset, SEEK_SET);
        fread(buf, bin->chunk_size, 1, bin->handle);
        return 1;
    } else {
        return 0;
    }
}

int
DAT_read_file_by_num(const dat_file* bin, uint32_t chunk_num, void* buf) {
    uint32_t offset = chunk_num * bin->chunk_size;
    if (chunk_num <= bin->num_chunks) {
        fseek(bin->handle, offset, SEEK_SET);
        fread(buf, bin->chunk_size, 1, bin->handle);
        return 1;
    } else {
        return 0;
    }
    return 1;
}

int
main(int argc, char** argv) {
    if (argc < NUM_ARGS + 1 /*binary itself*/) {
        printf("Incorrect usage!\n\t./datstrip input.dat openmenu.ini output.dat\n");
        return 1;
    }

    if (strcmp(argv[1], argv[3]) == 0) {
        printf("Incorrect usage: input and output cannot be the same file!\n");
        return 1;
    }
    /* Load INPUT DAT and parse */
    /* Basic Usage */
    dat_file input_bin;
    DAT_init(&input_bin);
    if (DAT_load_parse(&input_bin, argv[1])) {
        return -1;
    }

    /* Dump info and files */
    // DAT_info(&input_bin);

    /* Load INI and Parse entries */
    int entry_intersections = 0;
    if (list_read(argv[2])) {
        return -1;
    }
    int len = list_length();
    const gd_item* ini_entry;
    for (int i = 0; i < len; i++) {
        ini_entry = list_item_get(i);
        // DBG_PRINT("Checking %s: ", ini_entry->product);
        uint32_t offset = DAT_get_offset_by_ID(&input_bin, ini_entry->product);
        if (offset) {
            // DBG_PRINT("present!\n");
            entry_intersections++;
        } else {
            // DBG_PRINT("NOT present!\n");
        }
    }

    printf("Making new DAT with %d entries!\n", entry_intersections);

    file_header.chunk_size = input_bin.chunk_size;
    bin_items = malloc(sizeof(bin_item_raw) * entry_intersections);

    char temp_id[12];
    char temp_file[FILENAME_MAX];

    data_buf = malloc(file_header.chunk_size * entry_intersections);

    printf("Copying:");
    for (int i = 0; i < len; i++) {
        ini_entry = list_item_get(i);

        uint32_t offset = DAT_get_offset_by_ID(&input_bin, ini_entry->product);
        if (offset) {
            DAT_read_file_by_ID(&input_bin, ini_entry->product,
                                data_buf + (file_header.num_chunks * file_header.chunk_size));

            memcpy(&bin_items[file_header.num_chunks].ID, ini_entry->product, sizeof(bin_items->ID));
            bin_items[file_header.num_chunks].offset = file_header.num_chunks + 1;
            (void)file_header.num_chunks++;

#if 0
      DBG_PRINT("Copied[%d] %s\n", file_header.num_chunks, ini_entry->product);
#else
            printf("%s..", ini_entry->product);
#endif
        }
    }
    printf("done!\n");

    /* Using INI write new DAT only holding those entries */
    /* Setup file constraints */
    memcpy(&file_header.magic.rich.alpha, "DAT", 3);
    file_header.magic.rich.version = 1;
    file_header.padding0 = 0;

    out_fd = NULL;

    open_output(argv[3]);
    file_header.padding0 = 0;
    write_bin_file();
}