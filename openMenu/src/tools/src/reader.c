/*
 * File: reader.c
 * Project: dat_builder
 * File Created: Tuesday, 8th June 2021 2:47:35 pm
 * Author: Hayden Kowalchuk
 * -----
 * Copyright (c) 2021 Hayden Kowalchuk, Hayden Kowalchuk
 * License: BSD 3-clause "New" or "Revised" License, http://www.opensource.org/licenses/BSD-3-Clause
 */
#if defined(WIN32) || defined(WINNT)
#include <direct.h>
#define mkdir(A, B) mkdir(A)
#endif
#include <stdio.h>
#include <stdlib.h>
#include <sys/stat.h>

#define DEBUG (1)

#include <uthash.h>

#include <backend/dat_format.h>
#include <dbgprint.h>
/* Called:
./datread input.dat (-d)

Dumps all info about the container, optionally dump to files in input/
*/

#if defined(WIN32) || defined(WINNT)
#define PATH_SEP "\\"
#else
#define PATH_SEP "/"
#endif

#define NUM_ARGS (1)

void
DAT_dump(const dat_file* bin, const char* output) {
    char out_filename[FILENAME_MAX] = {0};
    mkdir(output, S_IRWXU | S_IRGRP | S_IXGRP | S_IROTH | S_IXOTH);
    uint8_t* file_buffer = malloc(bin->chunk_size);

    DBG_PRINT("BIN Stats:\nChunk Size: %u\nNum Chunks: %u\n\n", bin->chunk_size, bin->num_chunks);
    for (int i = 0; i < bin->num_chunks; i++) {
        DBG_PRINT("Record[%u] %s at 0x%X\n", bin->items[i].offset, bin->items[i].ID,
                  bin->items[i].offset * bin->chunk_size);
        /* Create output filename */
        strcpy(out_filename, output);
        strcat(out_filename, bin->items[i].ID);
        strcat(out_filename, ".pvr");

        /* Read chunk to buffer */
        int ret_f = fseek((FILE*)bin->handle, bin->items[i].offset * bin->chunk_size, SEEK_SET);
        int ret_r = fread(file_buffer, bin->chunk_size, 1, (FILE*)bin->handle);

        /* Write out */
        FILE* fd = fopen(out_filename, "wb");
        if (!fd) {
            perror("Could not open output file for writing");
            exit(2);
        }
        int ret_w = fwrite(file_buffer, bin->chunk_size, 1, fd);
        fclose(fd);
    }
}

int
main(int argc, char** argv) {
    int dump_files = 0;
    char output_dir[FILENAME_MAX];

    if (argc < NUM_ARGS + 1 /*binary itself*/) {
        printf("Incorrect usage!\n\t./datread input.dat (-d)\n");
        return 1;
    }

    if ((argc == NUM_ARGS + 2) && !strcasecmp(argv[2], "-d")) {
        dump_files = 1;
        strcpy(output_dir, "." PATH_SEP);
        strcat(output_dir, argv[1]);
        char* dir_basename = strrchr(output_dir, '.');
        if (dir_basename) {
            //*dir_basename = '\0';
            memcpy(dir_basename, PATH_SEP, strlen(PATH_SEP) + 1);
        }
    }

    /* Basic Usage */
    dat_file input_bin;
    DAT_init(&input_bin);
    DAT_load_parse(&input_bin, argv[1]);

    /* Dump info and files */
    if (dump_files) {
        DAT_dump(&input_bin, output_dir);
    } else {
        DAT_info(&input_bin);
    }

    /* Hashmap test */
    printf("\nSearching known:\n");
    const char* search = "T40502N";
    printf("Found %s at %X\n", search, DAT_get_offset_by_ID(&input_bin, search));

    printf("\nSearching missing:\n");
    const char* missing = "MISSING";
    printf("Found %s at %X\n", missing, DAT_get_offset_by_ID(&input_bin, missing));
}
