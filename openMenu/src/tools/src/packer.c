/*
 * File: packer.c
 * Project: dat_builder
 * File Created: Tuesday, 8th June 2021 2:32:46 pm
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

#include "dat_packer_interface.h"

/* Called:
./datpack FOLDER output.dat

packs the items in the folder into the output.bin
*/

#define NUM_ARGS (2)

/* Locals */
static bin_header file_header;
static bin_item_raw* bin_items;
static unsigned char* data_buf;

int
add_pvr_file(const char* path, const char* folder, struct stat* statptr) {
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
    printf("Working on %s\n", path);
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

    bin_items[file_header.num_chunks].offset = file_header.num_chunks + 1;
    (void)file_header.num_chunks++;

    printf("Added[%u] as %s\n", file_header.num_chunks, temp_id);
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
    iterate_dir(argv[1], add_pvr_file, &file_header, &bin_items);
    file_header.padding0 = 0;
    write_bin_file(&file_header, bin_items, data_buf);

    return EXIT_SUCCESS;
}