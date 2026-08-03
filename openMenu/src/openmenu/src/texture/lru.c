#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "lru.h"

#if DEBUG
#define DBG_PRINT(...) printf(__VA_ARGS__)
#else
#define DBG_PRINT(...)
#endif

/* Missing on sh-elf-gcc 9.1 ? */
char* strdup(const char* s);

/* LRU cache in C using uthash, by Jehiah Czebotar 2011 <jehiah@gmail.com>.
 * http://uthash.sourceforge.net/
 * Public domain, http://unlicense.org/ */

void
cache_set_size(cache_instance* cache, int size) {
    cache->cache_max_size = size;
}

void
cache_callback_userdata(cache_instance* cache, void* user) {
    cache->callback_data = user;
}

void
cache_callback_add(cache_instance* cache, user_add_cb callback) {
    cache->callback_add = callback;
}

void
cache_callback_del(cache_instance* cache, user_del_cb callback) {
    cache->callback_del = callback;
}

int
find_in_cache(cache_instance* cache, const char* key) {
    struct CacheEntry* entry;
    if (!cache || !key) {
        return -1;
    }
    HASH_FIND_STR(cache->cache, key, entry);
    if (entry) {
        /* Remove it, so the add below puts it back on the front of the list. */
        if (entry != cache->cache) {
            HASH_DELETE(hh, cache->cache, entry);
            HASH_ADD_STR(cache->cache, key, entry);
        }
        return entry->value;
    }
    return -1;
}

void
add_to_cache(cache_instance* cache, const char* key, int value) {
    DBG_PRINT("+%s( %s )\n", __func__, key);
    struct CacheEntry *entry, *tmp_entry, *new_entry;
    unsigned int cb_return = 0xFFFFFFFF;

    /* Call user function */
    if (cache->callback_add) {
        cb_return = (*cache->callback_add)(key, cache->callback_data);
    }

    entry = calloc(1, sizeof(struct CacheEntry));
    if (!entry) {
        /* printf("%s no free memory\n", __func__); */
        return;
    }
    entry->key = strdup(key);
    if (cb_return != 0xFFFFFFFF) {
        value = cb_return;
    }
    entry->value = value;
    new_entry = entry;
    HASH_ADD_STR(cache->cache, key, entry);

    /* Prune the cache down to cache_max_size. */
    if (HASH_COUNT(cache->cache) > cache->cache_max_size) {
        HASH_ITER(hh, cache->cache, entry, tmp_entry) {
            /* The loop runs in insertion order, so the first entry is the oldest. */
            HASH_DELETE(hh, cache->cache, entry);
            DBG_PRINT("-del_from_cache( %s )\n", key);
            if (cache->callback_del) {
                (*cache->callback_del)(entry->key, &entry->value, cache->callback_data);
                if (cache->callback_add) {
                    cb_return = (*cache->callback_add)(key, cache->callback_data);
                }
                new_entry->value = cb_return;
            }

            free(entry->key);
            free(entry);
            break;
        }
    }
}

void
empty_cache(cache_instance* cache) {
    struct CacheEntry *entry, *tmp_entry;
    HASH_ITER(hh, cache->cache, entry, tmp_entry) {
        /* Prune every entry. */
        HASH_DELETE(hh, cache->cache, entry);
        DBG_PRINT("-del_from_cache( %s )\n", key);
        if (cache->callback_del) {
            (*cache->callback_del)(entry->key, &entry->value, cache->callback_data);
        }

        free(entry->key);
        free(entry);
    }
}
