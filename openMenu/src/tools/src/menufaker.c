/*
 * File: menufaker.c
 * Project: tools
 * File Created: Friday, 18th June 2021 11:02:52 am
 * Author: Hayden Kowalchuk
 * -----
 * Copyright (c) 2021 Hayden Kowalchuk, Hayden Kowalchuk
 * License: BSD 3-clause "New" or "Revised" License, http://www.opensource.org/licenses/BSD-3-Clause
 */
#include <errno.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>

/* Called:
./menufaker filelist.csv

reads filelist.csv and creates a full OPENMENU.INI from it
*/

/* Compile:
x86_64-w64-mingw32-gcc menufaker.c -Os -s -o menufaker.exe
gcc menufaker.c -Os -s -o menufaker
*/

#ifdef WIN32
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
        bufptr = malloc(128);
        if (bufptr == NULL) {
            return -1;
        }
        size = 128;
    }
    p = bufptr;
    while (c != EOF) {
        if ((p - bufptr) > (size - 1)) {
            size = size + 128;
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
ini_write_header(int num_games, FILE* fd) {
    const char* items_header = "[ITEMS]\n"
                               "01.name=openMenu\n"
                               "01.disc=1/1\n"
                               "01.vga=1\n"
                               "01.region=JUE\n"
                               "01.version=V0.1.0\n"
                               "01.date=20210609\n"
                               "01.product=NEODC_1\n\n";
    fwrite("[OPENMENU]\nnum_items=", strlen("[OPENMENU]\nnum_items="), 1, fd);
    fprintf(fd, "%d\n\n", num_games);
    fwrite(items_header, strlen(items_header), 1, fd);
}

static void
ini_add_game(int num, const char* product_id, const char* name, FILE* fd) {
    const char* game_template = "%02d.name=%s\n"
                                "%02d.disc=1/1\n"
                                "%02d.vga=1\n"
                                "%02d.region=JUE\n"
                                "%02d.version=V0.1.0\n"
                                "%02d.date=20210609\n"
                                "%02d.product=%s\n\n";
    fprintf(fd, game_template, num, name, num, num, num, num, num, num, product_id);
}

#define NUM_ARGS (1)

int
main(int argc, char** argv) {
    if (argc < NUM_ARGS + 1 /*binary itself*/) {
        printf("Incorrect usage!\n\t./menufaker filelist.csv\n");
        return 1;
    }

    const char* csv_file = argv[1];
    char filename_temp[FILENAME_MAX];
    char filename_temp2[FILENAME_MAX];

    FILE *csv_fd, *ini_fd;
    char* line = malloc(FILENAME_MAX);
    size_t len = FILENAME_MAX;
    size_t read;

    csv_fd = fopen(csv_file, "rb");
    ini_fd = fopen("OPENMENU.INI", "w");
    if (csv_fd == 0 || ini_fd == 0) {
        exit(EXIT_FAILURE);
    }

    ini_write_header(256, ini_fd);
    int idx = 2;
    while ((read = getline(&line, &len, csv_fd)) != -1) {
        line[read - 1] = '\0';
        char* comma = strrchr(line, ',');
        *comma = '\0';

        char* game_name = line;
        char* product_id = comma + 1;

        char* game_name_end = strrchr(game_name, '(') - 1; /* removes (USA) */
        char* product_id_end = strrchr(product_id, '.');
        *(game_name_end) = '\0';
        *(product_id_end) = '\0';

        ini_add_game(idx++, product_id, game_name, ini_fd);
    }

    fclose(csv_fd);
    fclose(ini_fd);

    return EXIT_SUCCESS;
}