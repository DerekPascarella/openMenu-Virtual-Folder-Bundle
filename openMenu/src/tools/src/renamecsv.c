/*
 * File: renamecsv.c
 * Project: meta
 * File Created: Thursday, 17th June 2021 4:36:42 pm
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
./renamecsv FOLDER filelist.csv (-ext pvr)

reads filelist.csv and renames all files in Folder
if -ext is specified, ignores extension in csv and uses supplied one
*/

/* Compile:
x86_64-w64-mingw32-gcc renamecsv.c -Os -s -o renamecsv.exe
gcc renamecsv.c -Os -s -o renamecsv
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

#define NUM_ARGS (2)

int
main(int argc, char** argv) {
    if (argc < NUM_ARGS + 1 /*binary itself*/) {
        printf("Incorrect usage!\n\t./renamecsv FOLDER filelist.csv (-ext pvr)\n");
        return 1;
    }

    const char* folder = argv[1];
    const char* csv_file = argv[2];
    const char* new_extension = NULL;
    char filename_temp[FILENAME_MAX];
    char filename_temp2[FILENAME_MAX];
    int folder_len = strlen(folder);
    int replace_ext = (argc == NUM_ARGS + 2 + 1) && (strcmp(argv[3], "-ext") == 0);
    if (replace_ext) {
        new_extension = argv[4];
    }

    FILE* csv_fd;
    char* line = malloc(FILENAME_MAX);
    size_t len = FILENAME_MAX;
    size_t read;

    csv_fd = fopen(csv_file, "rb");
    if (!csv_fd) {
        exit(EXIT_FAILURE);
    }

    printf("REN: Renaming files in %s as per %s\n", folder, csv_file);

    while ((read = getline(&line, &len, csv_fd)) != -1) {
        line[read - 1] = '\0';
        char* comma = strrchr(line, ',');
        *comma = '\0';

        char* filename_cur = line;
        char* filename_new = comma + 1;
        char* filename_new_end = strrchr(filename_new, '\r');
        if (!filename_new_end) {
            filename_new_end = strrchr(filename_new, '\n');
        }
        if (filename_new_end) {
            *filename_new_end = '\0';
        }

        if (replace_ext) {
            char* filename_cur_ext = strrchr(filename_cur, '.') + 1;
            char* filename_new_ext = strrchr(filename_new, '.') + 1;
            memcpy(filename_cur_ext, new_extension, 3);
            memcpy(filename_new_ext, new_extension, 3);
            *(filename_cur_ext + 3) = '\0';
            *(filename_new_ext + 3) = '\0';
        }

        memcpy(filename_temp, folder, folder_len + 1);
        memcpy(filename_temp2, folder, folder_len + 1);
        strcat(filename_temp, filename_cur);
        strcat(filename_temp2, filename_new);
        struct stat buffer;
        int exists = (stat(filename_temp, &buffer) == 0);
        if (exists) {
            int ret = rename(filename_temp, filename_temp2);
            if (ret == 0) {
                /*Note: Too verbose */
                // printf("%s -> [%s]\n", filename_temp, filename_temp2);
            } else {
                printf("%s -> [%s]\n", filename_temp, filename_temp2);
                perror(filename_temp);
            }
        } else {
            printf("REN:Error %s missing!\n", filename_temp);
        }
    }

    fclose(csv_fd);

    return EXIT_SUCCESS;
}