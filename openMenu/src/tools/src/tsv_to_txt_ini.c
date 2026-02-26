/*
 * File: tsv_to_txt_ini.c
 * Project: tools
 * File Created: Tuesday, 22nd June 2021 10:35:52 pm
 * Author: Hayden Kowalchuk
 * -----
 * Copyright (c) 2021 Hayden Kowalchuk, Hayden Kowalchuk
 * License: BSD 3-clause "New" or "Revised" License, http://www.opensource.org/licenses/BSD-3-Clause
 */
#include <errno.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

/* Called:
./tsv2ini input.tsv FOLDER

reads the tsv sheet and creates ini metadata files in FOLDER based on serial
*/

#define NUM_ARGS (2)
#define MAX_LINE (512)

#if defined(WIN32) || defined(WINNT)
#define PATH_SEP "\\"
#else
#define PATH_SEP "/"
#endif

#ifdef WIN32
char*
strsep(char** stringp, const char* delim) {
    char* rv = *stringp;
    if (rv) {
        *stringp += strcspn(*stringp, delim);
        if (**stringp) {
            *(*stringp)++ = '\0';
        } else {
            *stringp = 0;
        }
    }
    return rv;
}

/* This code is public domain -- Will Hartung 4/9/09 */
size_t
getline(char** lineptr, size_t* n, FILE* stream) {
    char* bufptr = NULL;
    char* p = bufptr;
    size_t size;
    int c;

    if (lineptr == NULL) {
        return -1;
    }
    if (stream == NULL) {
        return -1;
    }
    if (n == NULL) {
        return -1;
    }
    bufptr = *lineptr;
    size = *n;

    c = fgetc(stream);
    if (c == EOF) {
        return -1;
    }
    if (bufptr == NULL) {
        bufptr = malloc(MAX_LINE);
        if (bufptr == NULL) {
            return -1;
        }
        size = MAX_LINE;
    }
    p = bufptr;
    while (c != EOF) {
        if ((p - bufptr) > (size - 1)) {
            size = size + MAX_LINE;
            bufptr = realloc(bufptr, size);
            if (bufptr == NULL) {
                return -1;
            }
        }
        *p++ = c;
        if (c == '\n') {
            break;
        }
        c = fgetc(stream);
    }

    *p++ = '\0';
    *lineptr = bufptr;
    *n = size;

    return p - bufptr - 1;
}
#endif

static void
write_ini(const char* filename, const char* players, const char* vmu_blocks, const char* accessories, const char* genre,
          const char* synopsis) {
    FILE* ini_fd = fopen(filename, "w");
    if (!ini_fd) {
        printf("Error: Couldn't write %s!\n", filename);
        return;
    }

    const char* _players = (players && (strlen(players) > 0) ? players : "0");
    const char* _vmu_blocks = (vmu_blocks && (strlen(vmu_blocks) > 0) ? vmu_blocks : "0");
    const char* _accessories = (accessories && (strlen(accessories) > 0) ? accessories : "0");
    const char* _genre = (genre && (strlen(genre) > 0) ? genre : "0");
    const char* _synopsis = (synopsis && (strlen(synopsis) > 0) ? synopsis : "0");
    _synopsis += (synopsis[0] == '"'); /* Strip leading quotation mark */

    const char* meta_template = "[ITEM]\n"
                                "num_players=%s\n"
                                "vmu_blocks=%s\n"
                                "accessories=%s\n"
                                "network=0\n"
                                "genre=%s\n"
                                "description=%s\n"
                                "padding1=0\n"
                                "padding2=0\n";
    fprintf(ini_fd, meta_template, _players, _vmu_blocks, _accessories, _genre, _synopsis);

    fclose(ini_fd);
}

int
main(int argc, char** argv) {
    if (argc < NUM_ARGS + 1 /*binary itself*/) {
        printf("Incorrect usage!\n\t./tsv2ini input.tsv FOLDER\n");
        return 1;
    }

    const char* tsv_file = argv[1];
    const char* output_folder = argv[2];

    char filename_temp[128];

    FILE* tsv_fd;
    char* line = malloc(MAX_LINE);
    size_t len = FILENAME_MAX;
    size_t read;

    tsv_fd = fopen(tsv_file, "rb");
    if (tsv_fd == 0) {
        exit(EXIT_FAILURE);
    }

    int i = 0;

    while ((read = getline(&line, &len, tsv_fd)) != -1) {
        line[read - 1] = '\0';
        /* Region | Players | VMU Blocks | Genre | Network | Accesories | Product ID | Name | Synopsis */

        const char* delim = "\t";
        char* token = strsep(&line, delim);
        const char* region = token;
        const char* players = strsep(&line, delim);
        const char* vmu_blocks = strsep(&line, delim);
        const char* genre = strsep(&line, delim);
        const char* network = strsep(&line, delim);
        const char* accesories = strsep(&line, delim);
        const char* product_no = strsep(&line, delim);
        const char* name = strsep(&line, delim);
        char* synopsis = strsep(&line, delim);

        /* Remove unneeded newline */
        char* synopsis_end = strrchr(synopsis, '\r');
        if (!synopsis_end) {
            synopsis_end = strrchr(synopsis, '\n');
        }
        if (synopsis_end) {
            *(synopsis_end - 1) = '\0';
        }

        memcpy(filename_temp, output_folder, strlen(output_folder) + 1);
        strcat(filename_temp, PATH_SEP);
        strcat(filename_temp, product_no);
        strcat(filename_temp, ".txt");

        write_ini(filename_temp, players, vmu_blocks, accesories, genre, synopsis);
        i++;
    }

    fclose(tsv_fd);

    printf("TSV: Wrote %d records out to %s!\n", i, output_folder);

    return EXIT_SUCCESS;
}