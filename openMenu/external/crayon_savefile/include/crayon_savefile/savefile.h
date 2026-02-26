#ifndef CRAYON_SAVEFILE_H
#define CRAYON_SAVEFILE_H

#include <inttypes.h> // For the %"PRIu32" part of printfs
#include <stdint.h>   // For the uintX_t types
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#if defined(_arch_dreamcast)

#include <dc/maple/vmu.h>
#include <dc/vmu_pkg.h>
#include <dc/vmufs.h>

#endif

#include "misc.h"
#include "peripheral.h"

// Var types the user passes into functions
enum {
    CRAYON_TYPE_DOUBLE = 0,
    CRAYON_TYPE_FLOAT,
    CRAYON_TYPE_UINT32,
    CRAYON_TYPE_SINT32,
    CRAYON_TYPE_UINT16,
    CRAYON_TYPE_SINT16,
    CRAYON_TYPE_UINT8,
    CRAYON_TYPE_SINT8,
    CRAYON_TYPE_CHAR,
    CRAYON_NUM_TYPES // This will be the number of types we have available
};

// This is never accessed directly by the user, but it will contain all of you variables that will get saved
typedef struct crayon_savefile_data {
    double* doubles;
    float* floats;
    uint32_t* u32;
    int32_t* s32;
    uint16_t* u16;
    int16_t* s16;
    uint8_t* u8;
    int8_t* s8;
    char* chars;

    uint32_t lengths[CRAYON_NUM_TYPES]; // The lengths are in the order they appear above
    uint32_t size;
} crayon_savefile_data_t;

// If you know you won't have many versions, you can change this to a uint8_t or uint16_t
// But don't change it mid way through a project otherwise everything will die and sad
#define crayon_savefile_version_t uint32_t

typedef struct crayon_savefile_history {
    uint32_t id;

    uint8_t data_type;
    uint32_t data_length;
    crayon_savefile_version_t version_added;
    crayon_savefile_version_t version_removed; // If greater than latest version, it hasn't been removed

    // Contains the address of the user's pointer so we can re-assign it in solidify()
    union {
        double** t_double;
        float** t_float;
        uint32_t** t_u32;
        int32_t** t_s32;
        uint16_t** t_u16;
        int16_t** t_s16;
        uint8_t** t_u8;
        int8_t** t_s8;
        char** t_char;
    } data_ptr;

    struct crayon_savefile_history* next;
} crayon_savefile_history_t;

// Only Dreamcast has the descriptions
enum {
    CRAYON_SF_STRING_FILENAME = 0,
    CRAYON_SF_STRING_APP_ID,
#if defined(_arch_dreamcast)
    CRAYON_SF_STRING_SHORT_DESC,
    CRAYON_SF_STRING_LONG_DESC,
#endif
    CRAYON_SF_NUM_DETAIL_STRINGS // 4 on Dreamcast, 2 on PC
};

#if defined(_arch_dreamcast)

#define CRAYON_SF_NUM_SAVE_DEVICES 8
#define CRAYON_SF_HDR_SIZE         sizeof(vmu_hdr_t)

#elif defined(_arch_pc)

#define CRAYON_SF_NUM_SAVE_DEVICES 1

#define CRAYON_SF_HDR_SIZE         (16 + 16) // Must be a multiple of 4 bytes long

typedef struct crayon_savefile_hdr {
    char name[16]; // Not actually used, but useful if you read a savefile with a hex editor
        // It should always be set to "CRAYON SAVEFILE" with a null terminator at the end
    char app_id[16];
} crayon_savefile_hdr_t;

#else

#error "UNSUPPORTED ARCH"

#endif

// The struct that contains the savefile details info.
typedef struct crayon_savefile_details {
    // The version of the most recent savefile version known to the program
    crayon_savefile_version_t latest_version;

    // The struct that contains the true data the user will indirectly manipulate
    crayon_savefile_data_t savedata;

    // All the strings we have. Different platforms might have a different strings
    char* strings[CRAYON_SF_NUM_DETAIL_STRINGS];

    // The following 3 variables are bitmaps
    uint8_t present_devices;       // Any savefile device detected regardless of present save
    uint8_t present_savefiles;     // Any savefile present, regardless of version or valid-ness
    uint8_t upgradable_to_current; // Any savefile from current or previous version

    crayon_savefile_version_t savefile_versions[CRAYON_SF_NUM_SAVE_DEVICES]; // Stores the savefiles versions
                                                                             // Zero is not-valid/present

    // This tells us what storage/save device we save to. 8 for Dreamcast, 1 for PC.
    // First save device is 0, but -1 is if we choose no device
    int8_t save_device_id;

    // Internal linked list to record the history of the savefile. Used for migrating old saves to new format
    crayon_savefile_history_t* history;
    crayon_savefile_history_t* history_tail; // Only used to speed stuff the history building process
    int32_t num_vars;                        // The length of the linked list

    // The functions the user provides for setting the default values of a new savefile and
    // how to handle the migration/upgrade of older savefiles to the new system
    void (*default_values_func)();
    int8_t (*upgrade_savefile_func)(void**, crayon_savefile_version_t, crayon_savefile_version_t);

// Dreamcast exclusive variables
#if defined(_arch_dreamcast)

    // Savefile icon bitmap and palette
    uint8_t* icon_data;
    uint16_t* icon_palette;
    uint8_t icon_anim_count;  // Decided to not go with a uint16_t since the Dreamcast BIOS
                              // will only work with up to 3 icons
    uint16_t icon_anim_speed; // Set to "0" for no animation (1st frame only)

    // Appears when selecting a save in the DC's BIOS
    uint8_t* eyecatcher_data;
    uint8_t eyecatcher_type; // PAL4BPP (4 Blocks), PAL8BPP (8.875 Blocks), bitmap ARGB4444 (15.75 Blocks)

#endif
} crayon_savefile_details_t;

//---------------Stuff the user should be calling----------------

// First savefile function you should call. It tells the system where to look for save devices.
// On Dreamcast this is always "/vmu/" and it will ignore the param
int8_t crayon_savefile_set_base_path(char* path);

// When you create a new savefile details struct, call this function before doing anything else
// The last two parameters are function pointers to user's functions.
int8_t crayon_savefile_init_savefile_details(crayon_savefile_details_t* details, const char* save_name,
                                             crayon_savefile_version_t latest_version, void (*default_values_func)(),
                                             int8_t (*upgrade_savefile_func)(void**, crayon_savefile_version_t,
                                                                             crayon_savefile_version_t));

// Used for setting the strings in the savefile header (Use the macros below)
int8_t crayon_savefile_set_hdr_string(crayon_savefile_details_t* details, const char* string, uint8_t string_id);

// Don't forget to set these strings for your savefile's header
#define crayon_savefile_set_app_id(details, string)                                                                    \
    crayon_savefile_set_hdr_string(details, string, CRAYON_SF_STRING_APP_ID);

#if defined(_arch_dreamcast)

#define crayon_savefile_set_short_desc(details, string)                                                                \
    crayon_savefile_set_hdr_string(details, string, CRAYON_SF_STRING_SHORT_DESC);
#define crayon_savefile_set_long_desc(details, string)                                                                 \
    crayon_savefile_set_hdr_string(details, string, CRAYON_SF_STRING_LONG_DESC);

#elif defined(_arch_pc)

#define crayon_savefile_set_short_desc(details, string) 0;
#define crayon_savefile_set_long_desc(details, string)  0;

#else

#error "UNSUPPORTED ARCH"

#endif

// Sets the savefile icon. If it returns -1 either an internal error happened or you used too many frames of
// animation for the icon (Dreamcast max = 3 since the BIOS can't render 4+ frames)
int8_t crayon_savefile_set_icon(crayon_savefile_details_t* details, const char* image, const char* palette,
                                uint8_t icon_anim_count, uint16_t icon_anim_speed);

// Eyecatchers are only ever used on Dreamcast. Calling this on any other platform will instead instantly return
int8_t crayon_savefile_set_eyecatcher(crayon_savefile_details_t* details, const char* eyecatch_path);

// Return type is the id of the variable. Returns the id, but will also return 0 on error
// Note that if a variable still exists, for version_removed we set it to the latest version + 1
int32_t crayon_savefile_add_variable(crayon_savefile_details_t* details, void* data_ptr, uint8_t data_type,
                                     uint32_t length, crayon_savefile_version_t version_added,
                                     crayon_savefile_version_t version_removed);

// Once we finish adding variables, we can then build our actual savefile with this fuction
int8_t crayon_savefile_solidify(crayon_savefile_details_t* details);

// Returns 0 on success and -1 on failure. Handles loading and saving of savefiles
int8_t crayon_savefile_load_savedata(crayon_savefile_details_t* details);
int8_t crayon_savefile_save_savedata(crayon_savefile_details_t* details);

// This will delete the saved savefile.
int8_t crayon_savefile_delete_savedata(crayon_savefile_details_t* details, int8_t save_device_id);

// Calling this will also free any icon/eyecatcher
void crayon_savefile_free_details(crayon_savefile_details_t* details);

void crayon_savefile_free_base_path();

// This will let you choose a device for saving/loading. Will fail if you can't save/load to the device
// If invalid, it will set the device to -1
int8_t crayon_savefile_set_device(crayon_savefile_details_t* details, int8_t save_device_id);

// Will return one of the status of the device
#define CRAYON_SF_STATUS_NO_DEVICE   0
#define CRAYON_SF_STATUS_INVALID_SF  1 // When savefile version is zero
#define CRAYON_SF_STATUS_NO_SF_FULL  2
#define CRAYON_SF_STATUS_NO_SF_ROOM  3
#define CRAYON_SF_STATUS_OLD_SF_FULL 4
#define CRAYON_SF_STATUS_OLD_SF_ROOM 5
#define CRAYON_SF_STATUS_CURRENT_SF  6
#define CRAYON_SF_STATUS_FUTURE_SF   7
int8_t crayon_savefile_save_device_status(crayon_savefile_details_t* details, int8_t save_device_id);

// To quickly check if you can use a device for a current savefile. Returns 1 if yes, 0 if false
uint8_t crayon_savefile_is_device_ready(crayon_savefile_details_t* details, int8_t save_device_id);

//---------------------Internal stuff that the user should have to touch------------------------

// This will get the boolean flag for present device, present savefile and current savefile for any device
uint8_t crayon_savefile_get_device_bit(uint8_t device_bitmap, uint8_t save_device_id);

// Basically get/setting bits from a bitmap
// I wish I could make this and the getter inlines, but GCC refuses to let me do it :(
void crayon_savefile_set_device_bit(uint8_t* device_bitmap, uint8_t save_device_id);

// Takes a byte count and returns number of blocks needed to save it (Dreamcast)
uint16_t crayon_savefile_convert_bytes_to_blocks(uint32_t bytes);

// Returns the number of bytes your save file will need
uint32_t crayon_savefile_get_savefile_size(crayon_savefile_details_t* details);

// Gets the length of one of the user provided strings
uint16_t crayon_savefile_get_user_string_length(uint8_t string_id);

void crayon_savefile_serialise_savedata(crayon_savefile_details_t* details, uint8_t* buffer);
int8_t crayon_savefile_deserialise_savedata(crayon_savefile_details_t* details, uint8_t* data, uint32_t data_length);

// Returns the amount of free space on the device (In Bytes)
uint32_t crayon_savefile_devices_free_space(int8_t device_id);

// Returns a pointer to a dynamically allocated path on success
// Returns NULL if either the the save_device_id is invalid or failed malloc
char* crayon_savefile_get_device_path(crayon_savefile_details_t* details, int8_t save_device_id);

// Only really meant for debugging. It just prints all the values in the savedata struct
void __crayon_savefile_print_savedata(crayon_savefile_data_t* savedata);

void crayon_savefile_buffer_to_savedata(crayon_savefile_data_t* data, uint8_t* buffer);

// Returns 0 if a savefile on that device exists with no issues, -1 if there's an error/DNE
int8_t crayon_savefile_check_savedata(crayon_savefile_details_t* details, int8_t save_device_id);

// These functions will update the valid devices and/or the current savefile bitmaps
void crayon_savefile_update_all_device_infos(crayon_savefile_details_t* details);
int8_t crayon_savefile_update_device_info(crayon_savefile_details_t* details, int8_t save_device_id);

void crayon_savefile_free_icon(crayon_savefile_details_t* details);
void crayon_savefile_free_eyecatcher(crayon_savefile_details_t* details);
void crayon_savefile_free_savedata(crayon_savefile_data_t* savedata);

#endif
