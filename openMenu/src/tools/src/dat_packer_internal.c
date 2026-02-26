#include <ctype.h>
#include <dirent.h>
#include <errno.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <sys/types.h>
#include <unistd.h>

#include <backend/dat_format.h>
#include "dat_packer_interface.h"

static FILE* out_fd;

void
open_output(const char* path) {
    out_fd = fopen(path, "wb");
    if (!out_fd) {
        printf("ERR: unable to open %s for writing!\n", path);
    }
}

void
write_bin_file(bin_header* file_header, bin_item_raw* bin_items, void* data_buf) {
    printf("Writing:");
    /* Write header */
    printf("header..");
    fwrite(file_header, sizeof(bin_header), 1, out_fd);
    /* Write file list */
    printf("item list..");
    fwrite(bin_items, sizeof(bin_item_raw), file_header->num_chunks, out_fd);
    /* Write padding out to first chunk offset */
    printf("padding..");
    int padding_size = ((file_header->padding0 + 1) * file_header->chunk_size) - ftell(out_fd);
    char* nul = calloc(1, padding_size);
    fwrite(nul, padding_size, 1, out_fd);
    free(nul);
    /* Write out all chunks */
    if (ftell(out_fd) % file_header->chunk_size != 0) {
        printf("\nDAT:Corrupted Header while writing!\n");
        fclose(out_fd);
        return;
    }
    printf("chunks..");
    fwrite(data_buf, file_header->num_chunks * file_header->chunk_size, 1, out_fd);

    fclose(out_fd);
    printf("done!\n");
}

static int
print_cb(const char* path, const char* folder, struct stat* statptr) {
    printf("%s\n", path);
    return 0;
}

int
iterate_dir(const char* path, int (*file_cb)(const char*, const char*, struct stat*), bin_header* file_header,
            bin_item_raw** bin_items) {
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

    file_header->padding0 = num_files_found;
    *bin_items = malloc(sizeof(bin_item_raw) * num_files_found);

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