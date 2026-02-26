#include "crayon_savefile/savefile.h"

// Its a path to the folder where it will try to save to
char* __savefile_path = NULL;
uint16_t __savefile_path_length = 0;

int8_t
crayon_savefile_set_base_path(char* path) {
    if (__savefile_path) {
        free(__savefile_path);
    }

#if defined(_arch_dreamcast)

    (void)path;

    uint16_t length = 5; // "/vmu/"

    if (!(__savefile_path = malloc(length + 1))) {
        return -1;
    }

    strcpy(__savefile_path, "/vmu/");

#elif defined(_arch_pc)

    uint16_t length = strlen(path);

    if (!(__savefile_path = malloc(length + 1))) {
        return -1;
    }

    strcpy(__savefile_path, path);

#else

#error "UNSUPPORTED ARCH"

#endif

    __savefile_path_length = length;
    return 0;
}

// Make sure to call this first (And call the save icon and eyecatcher functions after since this overides them)
int8_t
crayon_savefile_init_savefile_details(crayon_savefile_details_t* details, const char* save_name,
                                      crayon_savefile_version_t latest_version, void (*default_values_func)(),
                                      int8_t (*upgrade_savefile_func)(void**, crayon_savefile_version_t,
                                                                      crayon_savefile_version_t)) {

    uint16_t save_name_length = strlen(save_name);

    details->latest_version = latest_version;

    // Could probably replace this with a memset zero
    details->savedata.doubles = NULL;
    details->savedata.floats = NULL;
    details->savedata.u32 = NULL;
    details->savedata.s32 = NULL;
    details->savedata.u16 = NULL;
    details->savedata.s16 = NULL;
    details->savedata.u8 = NULL;
    details->savedata.s8 = NULL;
    details->savedata.chars = NULL;
    uint8_t i;
    for (i = 0; i < CRAYON_NUM_TYPES; i++) {
        details->savedata.lengths[i] = 0;
    }
    details->savedata.size = 0;

    //Will be set later
    details->present_devices = 0;
    details->present_savefiles = 0;
    details->upgradable_to_current = 0;
    for (i = 0; i < CRAYON_SF_NUM_SAVE_DEVICES; i++) {
        details->savefile_versions[i] = 0;
    }

#if defined(_arch_dreamcast)

    details->icon_data = NULL;
    details->icon_palette = NULL;
    details->icon_anim_count = 0;

    details->eyecatcher_data = NULL;
    details->eyecatcher_type = VMUPKG_EC_NONE; // The default, no eyecatcher

#endif

    details->save_device_id = -1;

    details->history = NULL;
    details->history_tail = NULL;
    details->num_vars = 0;

    uint16_t str_lengths[CRAYON_SF_NUM_DETAIL_STRINGS];
    for (i = 0; i < CRAYON_SF_NUM_DETAIL_STRINGS; i++) {
        details->strings[i] = NULL;
        str_lengths[i] = crayon_savefile_get_user_string_length(i);
        details->strings[i] = malloc(sizeof(char) * str_lengths[i]);
    }

    uint8_t error = 0;
    for (i = 0; i < CRAYON_SF_NUM_DETAIL_STRINGS; i++) {
        if (!details->strings[i]) {
            error = 1;
        }
    }

    // Given string is too big (Plus 1 for null terminator) or a previous error occured
    if (error || save_name_length + 1 > str_lengths[CRAYON_SF_STRING_FILENAME]) {
        // Need to properly un-allocate stuff
        for (i = 0; i < CRAYON_SF_NUM_DETAIL_STRINGS; i++) {
            if (details->strings[i]) {
                free(details->strings[i]);
            }
        }

        return -1;
    }

    // Copy the savename
    strncpy(details->strings[CRAYON_SF_STRING_FILENAME], save_name, str_lengths[CRAYON_SF_STRING_FILENAME]);

    details->default_values_func = default_values_func;
    details->upgrade_savefile_func = upgrade_savefile_func;

    return 0;
}

int8_t
crayon_savefile_set_hdr_string(crayon_savefile_details_t* details, const char* string, uint8_t string_id) {
    uint16_t max_length = crayon_savefile_get_user_string_length(string_id);
    uint16_t string_length = strlen(string);
    if (max_length == 0 || string_length >= max_length) {
        return -1;
    }

    strncpy(details->strings[string_id], string, max_length);

    return 0;
}

int8_t
crayon_savefile_set_icon(crayon_savefile_details_t* details, const char* image, const char* palette,
                         uint8_t icon_anim_count, uint16_t icon_anim_speed) {
#if defined(_arch_dreamcast)

    // Since BIOS can't render more than 3 for some reason
    if (icon_anim_count > 3) {
        return -1;
    }

    FILE* fp = fopen(image, "rb");
    uint32_t size;

    if (!fp) {
        return -1;
    }

    // Get the size of the file
    fseek(fp, 0, SEEK_END);
    size = ftell(fp); // This will account for multi-frame icons
    fseek(fp, 0, SEEK_SET);

    if (!(details->icon_data = malloc(size))) {
        fclose(fp);
        return -1;
    }

    fread(details->icon_data, size, 1, fp);
    fclose(fp);

    //--------------------------------

    if (!(fp = fopen(palette, "rb"))) {
        return -1;
    }

    fseek(fp, 0, SEEK_END);
    size = ftell(fp);
    fseek(fp, 0, SEEK_SET);

    if (!(details->icon_palette = malloc(size))) {
        fclose(fp);
        return -1;
    }

    fread(details->icon_palette, size, 1, fp);
    fclose(fp);

    details->icon_anim_count = icon_anim_count;
    details->icon_anim_speed = icon_anim_speed;

#endif

    return 0;
}

int8_t
crayon_savefile_set_eyecatcher(crayon_savefile_details_t* details, const char* eyecatcher_path) {
#if defined(_arch_dreamcast)

    FILE* eyecatcher_data_file = fopen(eyecatcher_path, "rb");
    if (!eyecatcher_data_file) {
        return -1;
    }

    // Get the size of the file
    fseek(eyecatcher_data_file, 0, SEEK_END);
    int size_data = ftell(eyecatcher_data_file); // Confirming its the right size
    fseek(eyecatcher_data_file, 0, SEEK_SET);

    switch (size_data) {
        case 8064: details->eyecatcher_type = VMUPKG_EC_16BIT; break;
        case 4544: details->eyecatcher_type = VMUPKG_EC_256COL; break;
        case 2048: details->eyecatcher_type = VMUPKG_EC_16COL; break;
        default: fclose(eyecatcher_data_file); return -1;
    }

    if (!(details->eyecatcher_data = malloc(size_data))) {
        fclose(eyecatcher_data_file);
        return -1;
    }

    fread(details->eyecatcher_data, size_data, 1, eyecatcher_data_file);
    fclose(eyecatcher_data_file);

#endif

    return 0;
}

int32_t
crayon_savefile_add_variable(crayon_savefile_details_t* details, void* data_ptr, uint8_t data_type, uint32_t length,
                             crayon_savefile_version_t version_added, crayon_savefile_version_t version_removed) {

    if (details->num_vars == INT32_MAX) {
        printf("You managed to make 2,147,483,647 unique variables\nI think you're doing something wrong\n");
        return -1;
    }

    // data_type id doesn't map to any of our types
    if (data_type >= CRAYON_NUM_TYPES) {
        return -1;
    }

    crayon_savefile_history_t* var = malloc(sizeof(crayon_savefile_history_t));
    if (!var) {
        return -1;
    }

    // Add the new variable
    var->next = NULL;
    if (details->history_tail) { // Non-empty linked list
        details->history_tail->next = var;
        details->history_tail = var;
    } else { // Empty linked list
        details->history = var;
        details->history_tail = var;
    }

    var->id = details->num_vars;

    var->data_type = data_type;
    var->data_length = length;
    var->version_added = version_added;
    var->version_removed = version_removed;

    // We only extend the length if the variable still exists
    if (version_removed > details->latest_version) {
        details->savedata.lengths[var->data_type] += var->data_length;
        var->data_ptr.t_double = NULL;
    }

    // Store the pointer's address so we can change it later
    switch (var->data_type) {
        case CRAYON_TYPE_DOUBLE: var->data_ptr.t_double = (double**)data_ptr; break;
        case CRAYON_TYPE_FLOAT: var->data_ptr.t_float = (float**)data_ptr; break;
        case CRAYON_TYPE_UINT32: var->data_ptr.t_u32 = (uint32_t**)data_ptr; break;
        case CRAYON_TYPE_SINT32: var->data_ptr.t_s32 = (int32_t**)data_ptr; break;
        case CRAYON_TYPE_UINT16: var->data_ptr.t_u16 = (uint16_t**)data_ptr; break;
        case CRAYON_TYPE_SINT16: var->data_ptr.t_s16 = (int16_t**)data_ptr; break;
        case CRAYON_TYPE_UINT8: var->data_ptr.t_u8 = (uint8_t**)data_ptr; break;
        case CRAYON_TYPE_SINT8: var->data_ptr.t_s8 = (int8_t**)data_ptr; break;
        case CRAYON_TYPE_CHAR: var->data_ptr.t_char = (char**)data_ptr; break;
    }

    return details->num_vars++;
}

int8_t
crayon_savefile_solidify(crayon_savefile_details_t* details) {
    uint32_t* lengths = details->savedata.lengths;
    uint32_t indexes[CRAYON_NUM_TYPES] = {0};

    uint8_t error = 0;

    // Don't both allocating space for these if we aren't using them
    if (lengths[CRAYON_TYPE_DOUBLE]) {
        if (!(details->savedata.doubles = malloc(sizeof(double) * lengths[CRAYON_TYPE_DOUBLE]))) {
            error = 1;
        }
    }
    if (lengths[CRAYON_TYPE_FLOAT]) {
        if (!(details->savedata.floats = malloc(sizeof(float) * lengths[CRAYON_TYPE_FLOAT]))) {
            error = 1;
        }
    }
    if (lengths[CRAYON_TYPE_UINT32]) {
        if (!(details->savedata.u32 = malloc(sizeof(uint32_t) * lengths[CRAYON_TYPE_UINT32]))) {
            error = 1;
        }
    }
    if (lengths[CRAYON_TYPE_SINT32]) {
        if (!(details->savedata.s32 = malloc(sizeof(int32_t) * lengths[CRAYON_TYPE_SINT32]))) {
            error = 1;
        }
    }
    if (lengths[CRAYON_TYPE_UINT16]) {
        if (!(details->savedata.u16 = malloc(sizeof(uint16_t) * lengths[CRAYON_TYPE_UINT16]))) {
            error = 1;
        }
    }
    if (lengths[CRAYON_TYPE_SINT16]) {
        if (!(details->savedata.s16 = malloc(sizeof(int16_t) * lengths[CRAYON_TYPE_SINT16]))) {
            error = 1;
        }
    }
    if (lengths[CRAYON_TYPE_UINT8]) {
        if (!(details->savedata.u8 = malloc(sizeof(uint8_t) * lengths[CRAYON_TYPE_UINT8]))) {
            error = 1;
        }
    }
    if (lengths[CRAYON_TYPE_SINT8]) {
        if (!(details->savedata.s8 = malloc(sizeof(int8_t) * lengths[CRAYON_TYPE_SINT8]))) {
            error = 1;
        }
    }
    if (lengths[CRAYON_TYPE_CHAR]) {
        if (!(details->savedata.chars = malloc(sizeof(char) * lengths[CRAYON_TYPE_CHAR]))) {
            error = 1;
        }
    }

    if (error) {
        crayon_savefile_free_savedata(&details->savedata); // Note this also resets the lengths
        return -1;
    }

    crayon_savefile_history_t* var = details->history;
    while (var) {
        if (var->version_removed > details->latest_version) { // We only give space to vars that still exist
            switch (var->data_type) {
                case CRAYON_TYPE_DOUBLE:
                    *var->data_ptr.t_double = &details->savedata.doubles[indexes[var->data_type]];
                    break;
                case CRAYON_TYPE_FLOAT:
                    *var->data_ptr.t_float = &details->savedata.floats[indexes[var->data_type]];
                    break;
                case CRAYON_TYPE_UINT32: *var->data_ptr.t_u32 = &details->savedata.u32[indexes[var->data_type]]; break;
                case CRAYON_TYPE_SINT32: *var->data_ptr.t_s32 = &details->savedata.s32[indexes[var->data_type]]; break;
                case CRAYON_TYPE_UINT16: *var->data_ptr.t_u16 = &details->savedata.u16[indexes[var->data_type]]; break;
                case CRAYON_TYPE_SINT16: *var->data_ptr.t_s16 = &details->savedata.s16[indexes[var->data_type]]; break;
                case CRAYON_TYPE_UINT8: *var->data_ptr.t_u8 = &details->savedata.u8[indexes[var->data_type]]; break;
                case CRAYON_TYPE_SINT8: *var->data_ptr.t_s8 = &details->savedata.s8[indexes[var->data_type]]; break;
                case CRAYON_TYPE_CHAR: *var->data_ptr.t_char = &details->savedata.chars[indexes[var->data_type]]; break;
            }
            indexes[var->data_type] += var->data_length;
        }
        var = var->next;
    }

    // Set the default values with the user's function
    (*details->default_values_func)();

    details->savedata.size =
        sizeof(crayon_savefile_version_t) + (lengths[CRAYON_TYPE_DOUBLE] * sizeof(double))
        + (lengths[CRAYON_TYPE_FLOAT] * sizeof(float)) + (lengths[CRAYON_TYPE_UINT32] * sizeof(uint32_t))
        + (lengths[CRAYON_TYPE_SINT32] * sizeof(int32_t)) + (lengths[CRAYON_TYPE_UINT16] * sizeof(uint16_t))
        + (lengths[CRAYON_TYPE_SINT16] * sizeof(int16_t)) + (lengths[CRAYON_TYPE_UINT8] * sizeof(uint8_t))
        + (lengths[CRAYON_TYPE_SINT8] * sizeof(int8_t)) + (lengths[CRAYON_TYPE_CHAR] * sizeof(char));

    crayon_savefile_update_all_device_infos(details);

    // If no savefile was found, then set our save device to the first valid memcard
    int8_t status;
    uint8_t i;
    if (details->save_device_id == -1) {
        for (i = 0; i < CRAYON_SF_NUM_SAVE_DEVICES; i++) {

            status = crayon_savefile_save_device_status(details, i);
            if (status == CRAYON_SF_STATUS_CURRENT_SF || status == CRAYON_SF_STATUS_OLD_SF_ROOM
                || status == CRAYON_SF_STATUS_NO_SF_ROOM || status == CRAYON_SF_STATUS_FUTURE_SF) {
                details->save_device_id = i;
                break;
            }
        }
    }

#if CRAYON_DEBUG == 1

    printf("\n---SOLIDIFY'S ENDING STATS---\n");
    printf("SF Size: HDR %d BODY %" PRIu32 "\n", CRAYON_SF_HDR_SIZE, details->savedata.size);
    // Those quotes between the two lines is to allow the string to continue as normal over multiple lines
    printf("Version size: %" PRIu32 ". Savedata lengths: %" PRIu32 ", %" PRIu32 ", %" PRIu32 ", %" PRIu32 ","
           "%" PRIu32 ", %" PRIu32 ", %" PRIu32 ", %" PRIu32 ", %" PRIu32 "\n",
           (uint32_t)sizeof(crayon_savefile_version_t), details->savedata.lengths[0], details->savedata.lengths[1],
           details->savedata.lengths[2], details->savedata.lengths[3], details->savedata.lengths[4],
           details->savedata.lengths[5], details->savedata.lengths[6], details->savedata.lengths[7],
           details->savedata.lengths[8]);
    printf("Bitmaps: %d, %d, %d\n", details->present_devices, details->present_savefiles,
           details->upgradable_to_current);

    printf("Versions: ");
    for (i = 0; i < CRAYON_SF_NUM_SAVE_DEVICES - 1; i++) {
        printf("%" PRIu32 ", ", details->savefile_versions[i]);
    }
    printf("%" PRIu32 "\n---END---\n\n", details->savefile_versions[CRAYON_SF_NUM_SAVE_DEVICES - 1]);

#endif

    return 0;
}

int8_t
crayon_savefile_load_savedata(crayon_savefile_details_t* details) {
    // Only proceed if we can do something here
    int8_t status = crayon_savefile_save_device_status(details, details->save_device_id);
    if (status != CRAYON_SF_STATUS_CURRENT_SF && status != CRAYON_SF_STATUS_OLD_SF_ROOM
        && status != CRAYON_SF_STATUS_NO_SF_ROOM && status != CRAYON_SF_STATUS_FUTURE_SF) {
        return -1;
    }

    char* savename = crayon_savefile_get_device_path(details, details->save_device_id);
    if (!savename) {
        return -1;
    }

    // If the savefile DNE somehow, this will fail
    FILE* fp = fopen(savename, "rb");
    free(savename);
    if (!fp) {
        return -1;
    }

    fseek(fp, 0, SEEK_END);
    int pkg_size = ftell(fp);
    fseek(fp, 0, SEEK_SET);

    uint8_t* data = malloc(pkg_size);
    if (!data) {
        fclose(fp);
        return -1;
    }

    fread(data, pkg_size, 1, fp);
    fclose(fp);

#if defined(_arch_dreamcast)

    vmu_pkg_t pkg;
    if (vmu_pkg_parse(data, &pkg)) {
        // CRC is incorrect
        ;
    }

    // Check to see if pkg.data_len is actually correct or not
    ;

    // Read the pkg data into my struct
    uint8_t deserialise_result =
        crayon_savefile_deserialise_savedata(details, (uint8_t*)pkg.data, (uint32_t)pkg.data_len);

#elif defined(_arch_pc)

    // Call the endian function on "data"
    ;

    // Obtain the data length
    crayon_savefile_hdr_t hdr;

    crayon_misc_encode_to_buffer((uint8_t*)&hdr.name, data, sizeof(hdr.name));
    crayon_misc_encode_to_buffer((uint8_t*)&hdr.app_id, data + sizeof(hdr.name), sizeof(hdr.app_id));

    // Either it has the wrong size or the app ids somehow don't match
    // (The later should never trigger if you use this library right)
    if (strcmp(hdr.app_id, details->strings[CRAYON_SF_STRING_APP_ID])) {
        free(data);
        return -1;
    }

    // Read the pkg data into my struct
    // We use CRAYON_SF_HDR_SIZE to skip the header
    uint8_t deserialise_result =
        crayon_savefile_deserialise_savedata(details, data + CRAYON_SF_HDR_SIZE, pkg_size - CRAYON_SF_HDR_SIZE);

#else

#error "UNSUPPORTED ARCH"

#endif

    // NOTE: We don't set the current_savefile bit even if the load was successful since the saved save is still old

    free(data);
    return deserialise_result;
}

int8_t
crayon_savefile_save_savedata(crayon_savefile_details_t* details) {
    // Only proceed if we can actually save
    int8_t status = crayon_savefile_save_device_status(details, details->save_device_id);
    if (status != CRAYON_SF_STATUS_CURRENT_SF && status != CRAYON_SF_STATUS_OLD_SF_ROOM
        && status != CRAYON_SF_STATUS_NO_SF_ROOM && status != CRAYON_SF_STATUS_FUTURE_SF) {
        return -1;
    }

    char* savename = crayon_savefile_get_device_path(details, details->save_device_id);
    if (!savename) {
        return -1;
    }

    FILE* fp;

#if defined(_arch_dreamcast)

    uint8_t* data = malloc(details->savedata.size);
    if (!data) {
        free(savename);
        return -1;
    }

    crayon_savefile_serialise_savedata(details, data);

    vmu_pkg_t pkg;
    strncpy(pkg.desc_long, details->strings[CRAYON_SF_STRING_LONG_DESC], 32);
    strncpy(pkg.desc_short, details->strings[CRAYON_SF_STRING_SHORT_DESC], 16);
    strncpy(pkg.app_id, details->strings[CRAYON_SF_STRING_APP_ID], 16);
    pkg.icon_cnt = details->icon_anim_count;
    pkg.icon_anim_speed = details->icon_anim_speed;
    memcpy(pkg.icon_pal, details->icon_palette, 32);
    pkg.icon_data = details->icon_data;
    pkg.eyecatch_type = details->eyecatcher_type;
    pkg.eyecatch_data = details->eyecatcher_data; // If the type is zero, this will be NULL anyways
    pkg.data_len = details->savedata.size;
    pkg.data = data;

    int pkg_size;
    uint8_t* pkg_out; // Allocated in function below
    vmu_pkg_build(&pkg, &pkg_out, &pkg_size);

    free(data); // No longer needed

    // Can't open file for some reason
    fp = fopen(savename, "wb");
    free(savename);
    if (!fp) {
        free(pkg_out);
        return -1;
    }

    uint8_t write_res = (fwrite(pkg_out, sizeof(uint8_t), pkg_size, fp) != pkg_size);
    free(pkg_out);
    fclose(fp);
    if (write_res) {
        return -1;
    }

#elif defined(_arch_pc)

    uint8_t* data = malloc(CRAYON_SF_HDR_SIZE + details->savedata.size);
    if (!data) {
        free(savename);
        return -1;
    }

    crayon_savefile_serialise_savedata(details, data + CRAYON_SF_HDR_SIZE);

    strncpy((char*)data, "CRAYON SAVEFILE", sizeof(((crayon_savefile_hdr_t*)0)->name));
    uint16_t offset = sizeof(((crayon_savefile_hdr_t*)0)->name);

    uint8_t i;
    uint8_t str_length;
    for (i = CRAYON_SF_STRING_APP_ID; i < CRAYON_SF_NUM_DETAIL_STRINGS; i++) {
        str_length = crayon_savefile_get_user_string_length(i);
        strncpy((char*)data + offset, details->strings[i], str_length);
        offset += str_length;
    }

    // Endian-ify the data block (PC only)
    ;

    fp = fopen(savename, "wb");
    free(savename);
    if (!fp) {
        free(data);
        return -1;
    }

    uint32_t size = details->savedata.size + CRAYON_SF_HDR_SIZE;
    uint8_t write_res = (fwrite(data, sizeof(uint8_t), size, fp) != size);
    free(data);
    fclose(fp);
    if (write_res) {
        return -1;
    }

#else

#error "UNSUPPORTED ARCH"

#endif

    crayon_savefile_set_device_bit(&details->present_savefiles, details->save_device_id);
    crayon_savefile_set_device_bit(&details->upgradable_to_current, details->save_device_id);
    details->savefile_versions[details->save_device_id] = details->latest_version; //Is this line needed?

    return 0;
}

int8_t
crayon_savefile_delete_savedata(crayon_savefile_details_t* details, int8_t save_device_id) {
    // Only proceed if we can actually save
    int8_t status = crayon_savefile_save_device_status(details, save_device_id);
    if (status == CRAYON_SF_STATUS_INVALID_SF || status == CRAYON_SF_STATUS_NO_DEVICE
        || status == CRAYON_SF_STATUS_NO_SF_FULL || status == CRAYON_SF_STATUS_NO_SF_ROOM) {
        return -1;
    }

    uint8_t result = 0;

#if defined(_arch_dreamcast)

    maple_device_t* vmu;
    vec2_s8_t p_and_s = crayon_peripheral_dreamcast_get_port_and_slot(save_device_id);
    if (!(vmu = maple_enum_dev(p_and_s.x, p_and_s.y)) || !vmu->valid) { // Device not present or invalid
        return -1;
    }

    // I belive this function just takes the file name, or at least the name of it in /vmu/XX/
    result = vmufs_delete(vmu, details->strings[CRAYON_SF_STRING_FILENAME]);

#elif defined(_arch_pc)

    char* savename = crayon_savefile_get_device_path(details, save_device_id);
    if (!savename) {
        return -1;
    }

    result = remove(savename);

    free(savename);

#else

#error "UNSUPPORTED ARCH"

#endif

    crayon_savefile_update_device_info(details, save_device_id);

    return result;
}

void
crayon_savefile_free_details(crayon_savefile_details_t* details) {
    crayon_savefile_free_icon(details);
    crayon_savefile_free_eyecatcher(details);

    // Free up history
    crayon_savefile_history_t* curr = details->history;
    crayon_savefile_history_t* prev = curr;
    while (curr) {
        curr = curr->next;
        free(prev);
        prev = curr;
    }

    details->history = NULL;
    details->history_tail = NULL;

    // Free up the actual save data;
    crayon_savefile_free_savedata(&details->savedata);

    uint8_t i;
    for (i = 0; i < CRAYON_SF_NUM_DETAIL_STRINGS; i++) {
        if (details->strings[i]) {
            free(details->strings[i]);
        }
        details->strings[i] = NULL;
    }

    details->default_values_func = NULL;
    details->upgrade_savefile_func = NULL;

    return;
}

void
crayon_savefile_free_base_path() {
    if (__savefile_path) {
        free(__savefile_path);
        __savefile_path = NULL;
        __savefile_path_length = 0;
    }

    return;
}

int8_t
crayon_savefile_set_device(crayon_savefile_details_t* details, int8_t save_device_id) {
    if (save_device_id < 0 || save_device_id >= CRAYON_SF_NUM_SAVE_DEVICES) {
        details->save_device_id = -1;
        return -1;
    }

    if (crayon_savefile_is_device_ready(details, save_device_id)) {
        details->save_device_id = save_device_id;
        return 0;
    }
    details->save_device_id = -1;
    return -1;
}

int8_t
crayon_savefile_save_device_status(crayon_savefile_details_t* details, int8_t save_device_id) {
    if (save_device_id < 0 || save_device_id >= CRAYON_SF_NUM_SAVE_DEVICES) {
        return -1;
    }

    // Get the right bits
    uint8_t present_device = details->present_devices & (1 << save_device_id);
    uint8_t present_savefile = details->present_savefiles & (1 << save_device_id);
    uint8_t upgradable_to_current = details->upgradable_to_current & (1 << save_device_id);
    crayon_savefile_version_t version = details->savefile_versions[save_device_id];

    if (!present_device) {
        return CRAYON_SF_STATUS_NO_DEVICE;
    } else if (!present_savefile) {
        if (upgradable_to_current) {
            return CRAYON_SF_STATUS_NO_SF_ROOM;
        }
        return CRAYON_SF_STATUS_NO_SF_FULL;
    } else if (version > details->latest_version) {
        return CRAYON_SF_STATUS_FUTURE_SF;
    } else if (version == details->latest_version) {
        return CRAYON_SF_STATUS_CURRENT_SF;
    } else if (version < details->latest_version && version != 0) {
        if (upgradable_to_current) {
            return CRAYON_SF_STATUS_OLD_SF_ROOM;
        }
        return CRAYON_SF_STATUS_OLD_SF_FULL;
    } else { // If all of the above failed, the savefile must be invalid
        return CRAYON_SF_STATUS_INVALID_SF;
    }
}

uint8_t
crayon_savefile_is_device_ready(crayon_savefile_details_t* details, int8_t save_device_id) {
    int8_t status = crayon_savefile_save_device_status(details, save_device_id);
    if (status != CRAYON_SF_STATUS_CURRENT_SF && status != CRAYON_SF_STATUS_OLD_SF_ROOM
        && status != CRAYON_SF_STATUS_NO_SF_ROOM && status != CRAYON_SF_STATUS_FUTURE_SF) {
        return 0;
    }
    return 1;
}

uint8_t
crayon_savefile_get_device_bit(uint8_t device_bitmap, uint8_t save_device_id) {
    return (device_bitmap >> save_device_id) & 1;
}

void
crayon_savefile_set_device_bit(uint8_t* device_bitmap, uint8_t save_device_id) {
    *device_bitmap |= 1 << save_device_id;
}

// Rounds the number down to nearest multiple of 512, then adds 1 if there's a remainder
// (Function is only useful for Dreamcast)
uint16_t
crayon_savefile_convert_bytes_to_blocks(uint32_t bytes) {
    return (bytes >> 9) + !!(bytes & ((1 << 9) - 1));
}

// I used the "vmu_pkg_build" function's source as a guide for this. Doesn't work with compressed saves
uint32_t
crayon_savefile_get_savefile_size(crayon_savefile_details_t* details) {
#if defined(_arch_dreamcast)

// When vmu_eyecatch_size() is made non-static, switch over
#if 0

	uint16_t eyecatcher_size = vmu_eyecatch_size();
	if(eyecatcher_size < 0){return 0;}

#else

    uint16_t eyecatcher_size = 0;
    switch (details->eyecatcher_type) {
        case VMUPKG_EC_NONE: eyecatcher_size = 0; break;
        case VMUPKG_EC_16BIT: eyecatcher_size = 8064; break;
        case VMUPKG_EC_256COL: eyecatcher_size = 512 + 4032; break;
        case VMUPKG_EC_16COL: eyecatcher_size = 32 + 2016; break;
        default: return 0;
    }

#endif

    // Get the total number of bytes. Keep in mind we need to think about the icon/s and EC
    return CRAYON_SF_HDR_SIZE + (512 * details->icon_anim_count) + eyecatcher_size + details->savedata.size;

#elif defined(_arch_pc)

    return CRAYON_SF_HDR_SIZE + details->savedata.size;

#else

#error "UNSUPPORTED ARCH"

#endif
}

uint16_t
crayon_savefile_get_user_string_length(uint8_t string_id) {
    switch (string_id) {
        case CRAYON_SF_STRING_FILENAME:
#if defined(_arch_pc)

            return 256;

#elif defined(_arch_dreamcast)

            return 21 - 8; // The 8 is the /vmu/XX/ and the name itself can only be 13 chars (Last it null terminator)

#else

#error "UNSUPPORTED ARCH"

#endif
        case CRAYON_SF_STRING_APP_ID: return 16;

#if defined(_arch_dreamcast)

        case CRAYON_SF_STRING_SHORT_DESC: return 16;
        case CRAYON_SF_STRING_LONG_DESC: return 32;

#endif

        default: return 0;
    }
}

void
crayon_savefile_serialise_savedata(crayon_savefile_details_t* details, uint8_t* buffer) {
    crayon_savefile_data_t data = details->savedata;

    // Encode the version number
    crayon_misc_encode_to_buffer(buffer, (uint8_t*)&details->latest_version, sizeof(crayon_savefile_version_t));
    buffer += sizeof(crayon_savefile_version_t);

    // Next encode all doubles, then floats, then unsigned 32-bit ints, then signed 32-bit ints
    // Then 16-bit ints, 8-bit ints and finally the characters. No need to encode the lengths
    // since the history can tell us that
    crayon_misc_encode_to_buffer(buffer, (uint8_t*)data.doubles, sizeof(double) * data.lengths[CRAYON_TYPE_DOUBLE]);
    buffer += (sizeof(double) * data.lengths[CRAYON_TYPE_DOUBLE]);

    crayon_misc_encode_to_buffer(buffer, (uint8_t*)data.floats, sizeof(float) * data.lengths[CRAYON_TYPE_FLOAT]);
    buffer += (sizeof(float) * data.lengths[CRAYON_TYPE_FLOAT]);

    crayon_misc_encode_to_buffer(buffer, (uint8_t*)data.u32, sizeof(uint32_t) * data.lengths[CRAYON_TYPE_UINT32]);
    buffer += (sizeof(uint32_t) * data.lengths[CRAYON_TYPE_UINT32]);

    crayon_misc_encode_to_buffer(buffer, (uint8_t*)data.s32, sizeof(int32_t) * data.lengths[CRAYON_TYPE_SINT32]);
    buffer += (sizeof(int32_t) * data.lengths[CRAYON_TYPE_SINT32]);

    crayon_misc_encode_to_buffer(buffer, (uint8_t*)data.u16, sizeof(uint16_t) * data.lengths[CRAYON_TYPE_UINT16]);
    buffer += (sizeof(uint16_t) * data.lengths[CRAYON_TYPE_UINT16]);

    crayon_misc_encode_to_buffer(buffer, (uint8_t*)data.s16, sizeof(int16_t) * data.lengths[CRAYON_TYPE_SINT16]);
    buffer += (sizeof(int16_t) * data.lengths[CRAYON_TYPE_SINT16]);

    crayon_misc_encode_to_buffer(buffer, data.u8, sizeof(uint8_t) * data.lengths[CRAYON_TYPE_UINT8]);
    buffer += (sizeof(uint8_t) * data.lengths[CRAYON_TYPE_UINT8]);

    crayon_misc_encode_to_buffer(buffer, (uint8_t*)data.s8, sizeof(int8_t) * data.lengths[CRAYON_TYPE_SINT8]);
    buffer += (sizeof(int8_t) * data.lengths[CRAYON_TYPE_SINT8]);

    crayon_misc_encode_to_buffer(buffer, (uint8_t*)data.chars, sizeof(char) * data.lengths[CRAYON_TYPE_CHAR]);
    // buffer += (sizeof(char) * data.lengths[CRAYON_TYPE_CHAR]);

    return;
}

// Assume the buffer has the correct endian-ness going into this
int8_t
crayon_savefile_deserialise_savedata(crayon_savefile_details_t* details, uint8_t* data, uint32_t data_length) {
    // Same as serialiser, but instead we extract the variables and version from the buffer
    crayon_savefile_data_t* new_savedata = &details->savedata;

    // Decode the version number
    crayon_savefile_version_t loaded_version;
    crayon_misc_encode_to_buffer((uint8_t*)&loaded_version, data, sizeof(crayon_savefile_version_t));

    // Doing this makes future calculations easier
    data += sizeof(crayon_savefile_version_t);
    data_length -= sizeof(crayon_savefile_version_t);

    // Version zero is reserved for "invalid/not present"
    if (loaded_version == 0) {
        return -1;
    }

    // If its an older savefile, put it in the crayon_savefile_data_t format, make that union pointer array
    // and call the user's upgrade function
    if (loaded_version < details->latest_version) {
        crayon_savefile_data_t old_savedata;

        // Sets all the lengths to zero and the pointers to null
        memset(&old_savedata, 0, sizeof(crayon_savefile_data_t));

        // Need to use history to find the lengths of all 9 arrays
        crayon_savefile_history_t* curr = details->history;
        while (curr != NULL) {
            // If the variable exists in the loaded version, add it to our length
            if (loaded_version >= curr->version_added && loaded_version < curr->version_removed) {
                old_savedata.lengths[curr->data_type] += curr->data_length;
            }

            curr = curr->next;
        }

        uint32_t expected_size = (old_savedata.lengths[CRAYON_TYPE_DOUBLE] * sizeof(double))
                                 + (old_savedata.lengths[CRAYON_TYPE_FLOAT] * sizeof(float))
                                 + (old_savedata.lengths[CRAYON_TYPE_UINT32] * sizeof(uint32_t))
                                 + (old_savedata.lengths[CRAYON_TYPE_SINT32] * sizeof(int32_t))
                                 + (old_savedata.lengths[CRAYON_TYPE_UINT16] * sizeof(uint16_t))
                                 + (old_savedata.lengths[CRAYON_TYPE_SINT16] * sizeof(int16_t))
                                 + (old_savedata.lengths[CRAYON_TYPE_UINT8] * sizeof(uint8_t))
                                 + (old_savedata.lengths[CRAYON_TYPE_SINT8] * sizeof(int8_t))
                                 + (old_savedata.lengths[CRAYON_TYPE_CHAR] * sizeof(char));

        // The size is off, the savefile must have been tampered with
        if (expected_size != data_length) {
#if CRAYON_DEBUG == 1

            printf("Deserial, wrong sizes: %" PRIu32 " %" PRIu32 "\n", expected_size, data_length);

#endif

            return -1;
        }

        //Allocate space for our arrays
        old_savedata.doubles = malloc(sizeof(double) * old_savedata.lengths[CRAYON_TYPE_DOUBLE]);
        old_savedata.floats = malloc(sizeof(float) * old_savedata.lengths[CRAYON_TYPE_FLOAT]);
        old_savedata.u32 = malloc(sizeof(uint32_t) * old_savedata.lengths[CRAYON_TYPE_UINT32]);
        old_savedata.s32 = malloc(sizeof(int32_t) * old_savedata.lengths[CRAYON_TYPE_SINT32]);
        old_savedata.u16 = malloc(sizeof(uint16_t) * old_savedata.lengths[CRAYON_TYPE_UINT16]);
        old_savedata.s16 = malloc(sizeof(int16_t) * old_savedata.lengths[CRAYON_TYPE_SINT16]);
        old_savedata.u8 = malloc(sizeof(uint8_t) * old_savedata.lengths[CRAYON_TYPE_UINT8]);
        old_savedata.s8 = malloc(sizeof(int8_t) * old_savedata.lengths[CRAYON_TYPE_SINT8]);
        old_savedata.chars = malloc(sizeof(char) * old_savedata.lengths[CRAYON_TYPE_CHAR]);

        void** array = malloc(sizeof(void*) * details->num_vars);

        // Check if any of those mallocs failed, if so terminate
        if (!old_savedata.doubles || !old_savedata.floats || !old_savedata.u32 || !old_savedata.s32 || !old_savedata.u16
            || !old_savedata.s16 || !old_savedata.u8 || !old_savedata.s8 || !old_savedata.chars || !array) {
            crayon_savefile_free_savedata(&old_savedata);

            if (array) {
                free(array);
            }

            return -1;
        }

        // Convert the buffer into our savedata struct
        crayon_savefile_buffer_to_savedata(&old_savedata, data);

        // Go through the history again, but now set the pointers for each variable. Note variables that DNE in
        // This version should be set to NULL
        // Also copy over the common vars from the old savedata to the new one
        curr = details->history;
        uint32_t i, index = 0;
        uint32_t pointers[CRAYON_NUM_TYPES] = {0};
        while (curr != NULL) {
            // If the variable exists in the loaded version
            if (loaded_version >= curr->version_added && loaded_version < curr->version_removed) {
                switch (curr->data_type) {
                    case CRAYON_TYPE_DOUBLE: array[index] = old_savedata.doubles + pointers[curr->data_type]; break;
                    case CRAYON_TYPE_FLOAT: array[index] = old_savedata.floats + pointers[curr->data_type]; break;
                    case CRAYON_TYPE_UINT32: array[index] = old_savedata.u32 + pointers[curr->data_type]; break;
                    case CRAYON_TYPE_SINT32: array[index] = old_savedata.s32 + pointers[curr->data_type]; break;
                    case CRAYON_TYPE_UINT16: array[index] = old_savedata.u16 + pointers[curr->data_type]; break;
                    case CRAYON_TYPE_SINT16: array[index] = old_savedata.s16 + pointers[curr->data_type]; break;
                    case CRAYON_TYPE_UINT8: array[index] = old_savedata.u8 + pointers[curr->data_type]; break;
                    case CRAYON_TYPE_SINT8: array[index] = old_savedata.s8 + pointers[curr->data_type]; break;
                    case CRAYON_TYPE_CHAR: array[index] = old_savedata.chars + pointers[curr->data_type]; break;
                    default:
                        curr = curr->next;
                        index++;
                        continue;
                }
                pointers[curr->data_type] += curr->data_length;

                //Current variables and setting them to variables that existed in that version
                if (curr->version_removed > details->latest_version) {
                    switch (curr->data_type) {
                        case CRAYON_TYPE_DOUBLE:
                            for (i = 0; i < curr->data_length; i++) {
                                (*curr->data_ptr.t_double)[i] = ((double*)array[index])[i];
                            }
                            break;
                        case CRAYON_TYPE_FLOAT:
                            for (i = 0; i < curr->data_length; i++) {
                                (*curr->data_ptr.t_float)[i] = ((float*)array[index])[i];
                            }
                            break;
                        case CRAYON_TYPE_UINT32:
                            for (i = 0; i < curr->data_length; i++) {
                                (*curr->data_ptr.t_u32)[i] = ((uint32_t*)array[index])[i];
                            }
                            break;
                        case CRAYON_TYPE_SINT32:
                            for (i = 0; i < curr->data_length; i++) {
                                (*curr->data_ptr.t_s32)[i] = ((int32_t*)array[index])[i];
                            }
                            break;
                        case CRAYON_TYPE_UINT16:
                            for (i = 0; i < curr->data_length; i++) {
                                (*curr->data_ptr.t_u16)[i] = ((uint16_t*)array[index])[i];
                            }
                            break;
                        case CRAYON_TYPE_SINT16:
                            for (i = 0; i < curr->data_length; i++) {
                                (*curr->data_ptr.t_s16)[i] = ((int16_t*)array[index])[i];
                            }
                            break;
                        case CRAYON_TYPE_UINT8:
                            for (i = 0; i < curr->data_length; i++) {
                                (*curr->data_ptr.t_u8)[i] = ((uint8_t*)array[index])[i];
                            }
                            break;
                        case CRAYON_TYPE_SINT8:
                            for (i = 0; i < curr->data_length; i++) {
                                (*curr->data_ptr.t_s8)[i] = ((int8_t*)array[index])[i];
                            }
                            break;
                        case CRAYON_TYPE_CHAR:
                            for (i = 0; i < curr->data_length; i++) {
                                (*curr->data_ptr.t_char)[i] = ((char*)array[index])[i];
                            }
                            break;
                    }
                }
            } else { // Set the pointer to NULL if it doesn't exist in the old savefile
                array[index] = NULL;
            }

            index++;
            curr = curr->next;
        }

        // Call the user function to handle old to new savedata transfers
        (*details->upgrade_savefile_func)(array, loaded_version, details->latest_version);

#if CRAYON_DEBUG == 1

        printf("OLD v%" PRIu32 "\n", loaded_version);
        // size update isn't needed for calculations, only to show correct value in function
        old_savedata.size = sizeof(crayon_savefile_version_t) + (8 * old_savedata.lengths[0])
                            + (4 * (old_savedata.lengths[1] + old_savedata.lengths[2] + old_savedata.lengths[3]))
                            + (2 * (old_savedata.lengths[4] + old_savedata.lengths[5])) + old_savedata.lengths[6]
                            + old_savedata.lengths[7] + old_savedata.lengths[8];
        __crayon_savefile_print_savedata(&old_savedata);
        printf("\n");

#endif

        // Free the array
        free(array);

        // Free the old savedata arrays too
        crayon_savefile_free_savedata(&old_savedata);
    } else if (loaded_version > details->latest_version) {
        // Future version - read only current version's data, ignore extra
        uint32_t expected_size = details->savedata.size - sizeof(crayon_savefile_version_t);
        if (data_length < expected_size) {
            return -1;
        }

        crayon_savefile_buffer_to_savedata(new_savedata, data);
    } else { // If same version
        // The size is wrong, savefile has been tampered with
        if (details->savedata.size - sizeof(crayon_savefile_version_t) != data_length) {
            return -1;
        }

        crayon_savefile_buffer_to_savedata(new_savedata, data);
    }

#if CRAYON_DEBUG == 1

    printf("NEW v%" PRIu32 "\n", details->latest_version);
    __crayon_savefile_print_savedata(new_savedata);
    printf("\n");

#endif

    return 0;
}

uint32_t
crayon_savefile_devices_free_space(int8_t device_id) {
#if defined(_arch_dreamcast)

    vec2_s8_t port_and_slot = crayon_peripheral_dreamcast_get_port_and_slot(device_id);

    maple_device_t* vmu;
    vmu = maple_enum_dev(port_and_slot.x, port_and_slot.y);
    if (!vmu || !vmu->valid) { // Device not found or invalid
        return 0;
    }

    return vmufs_free_blocks(vmu) * 512;

#elif defined(_arch_pc)

    // Yeah, idk how to get system's remaining space size in C without exec/system
    return INT32_MAX;

#else

#error "UNSUPPORTED ARCH"

#endif
}

// It will construct the full string for you
char*
crayon_savefile_get_device_path(crayon_savefile_details_t* details, int8_t save_device_id) {
    if (save_device_id < 0 || save_device_id >= CRAYON_SF_NUM_SAVE_DEVICES) {
        return NULL;
    }

#if defined(_arch_dreamcast)

    // The 3 comes from the "a0/" part
    uint32_t length = __savefile_path_length + 3 + strlen(details->strings[CRAYON_SF_STRING_FILENAME]) + 1;

#elif defined(_arch_pc)

    uint32_t length = __savefile_path_length + strlen(details->strings[CRAYON_SF_STRING_FILENAME]) + 1;

#else

#error "UNSUPPORTED ARCH"

#endif

    char* path = malloc(length * sizeof(char));
    if (!path) {
        return NULL;
    }

    strcpy(path, __savefile_path);

#if defined(_arch_dreamcast)

    vec2_s8_t port_and_slot = crayon_peripheral_dreamcast_get_port_and_slot(save_device_id);
    if (port_and_slot.x == -1) { // Due to first return NULL, this should never trigger
        free(path);
        return NULL;
    }

    // Faster than constructing the string and then strcat-ing it
    uint32_t curr_length = strlen(path);
    path[curr_length] = port_and_slot.x + 'a';
    path[curr_length + 1] = port_and_slot.y + '0';
    path[curr_length + 2] = '/';
    path[curr_length + 3] = '\0';

#endif

    strcat(path, details->strings[CRAYON_SF_STRING_FILENAME]);

    return path;
}

// NOTE: You should never need to access these variables directly. I'm only doing so for debugging purposes
void
__crayon_savefile_print_savedata(crayon_savefile_data_t* savedata) {
#if CRAYON_DEBUG == 1

    uint32_t i;
    printf("---START---\n\n");

    printf("LENGTHS\n");
    for (i = 0; i < CRAYON_NUM_TYPES; i++) {
        printf("%" PRIu32 ", ", savedata->lengths[i]);
    }
    printf("\n");
    printf("SIZE: %" PRIu32 "\n\n", savedata->size);

    printf("Double\n");
    for (i = 0; i < savedata->lengths[CRAYON_TYPE_DOUBLE]; i++) {
        printf("%lf, ", savedata->doubles[i]);
    }
    printf("\n");

    printf("Float\n");
    for (i = 0; i < savedata->lengths[CRAYON_TYPE_FLOAT]; i++) {
        printf("%f, ", savedata->floats[i]);
    }
    printf("\n");

    printf("u32\n");
    for (i = 0; i < savedata->lengths[CRAYON_TYPE_UINT32]; i++) {
        printf("%" PRIu32 ", ", savedata->u32[i]);
    }
    printf("\n");

    printf("s32\n");
    for (i = 0; i < savedata->lengths[CRAYON_TYPE_SINT32]; i++) {
        printf("%" PRId32 ", ", savedata->s32[i]);
    }
    printf("\n");

    printf("u16\n");
    for (i = 0; i < savedata->lengths[CRAYON_TYPE_UINT16]; i++) {
        printf("%" PRIu16 ", ", savedata->u16[i]);
    }
    printf("\n");

    printf("s16\n");
    for (i = 0; i < savedata->lengths[CRAYON_TYPE_SINT16]; i++) {
        printf("%" PRId16 ", ", savedata->s16[i]);
    }
    printf("\n");

    printf("u8\n");
    for (i = 0; i < savedata->lengths[CRAYON_TYPE_UINT8]; i++) {
        printf("%" PRIu8 ", ", savedata->u8[i]);
    }
    printf("\n");

    printf("s8\n");
    for (i = 0; i < savedata->lengths[CRAYON_TYPE_SINT8]; i++) {
        printf("%" PRId8 ", ", savedata->s8[i]);
    }
    printf("\n");

    printf("Chars\n");
    for (i = 0; i < savedata->lengths[CRAYON_TYPE_CHAR]; i++) {
        printf("%c", savedata->chars[i]);
    }
    printf("\n---END---\n");

#endif

    return;
}

void
crayon_savefile_buffer_to_savedata(crayon_savefile_data_t* data, uint8_t* buffer) {
    crayon_misc_encode_to_buffer((uint8_t*)data->doubles, buffer, sizeof(double) * data->lengths[CRAYON_TYPE_DOUBLE]);
    buffer += sizeof(double) * data->lengths[CRAYON_TYPE_DOUBLE];

    crayon_misc_encode_to_buffer((uint8_t*)data->floats, buffer, sizeof(float) * data->lengths[CRAYON_TYPE_FLOAT]);
    buffer += sizeof(float) * data->lengths[CRAYON_TYPE_FLOAT];

    crayon_misc_encode_to_buffer((uint8_t*)data->u32, buffer, sizeof(uint32_t) * data->lengths[CRAYON_TYPE_UINT32]);
    buffer += sizeof(uint32_t) * data->lengths[CRAYON_TYPE_UINT32];

    crayon_misc_encode_to_buffer((uint8_t*)data->s32, buffer, sizeof(int32_t) * data->lengths[CRAYON_TYPE_SINT32]);
    buffer += sizeof(int32_t) * data->lengths[CRAYON_TYPE_SINT32];

    crayon_misc_encode_to_buffer((uint8_t*)data->u16, buffer, sizeof(uint16_t) * data->lengths[CRAYON_TYPE_UINT16]);
    buffer += sizeof(uint16_t) * data->lengths[CRAYON_TYPE_UINT16];

    crayon_misc_encode_to_buffer((uint8_t*)data->s16, buffer, sizeof(int16_t) * data->lengths[CRAYON_TYPE_SINT16]);
    buffer += sizeof(int16_t) * data->lengths[CRAYON_TYPE_SINT16];

    crayon_misc_encode_to_buffer(data->u8, buffer, sizeof(uint8_t) * data->lengths[CRAYON_TYPE_UINT8]);
    buffer += sizeof(uint8_t) * data->lengths[CRAYON_TYPE_UINT8];

    crayon_misc_encode_to_buffer((uint8_t*)data->s8, buffer, sizeof(int8_t) * data->lengths[CRAYON_TYPE_SINT8]);
    buffer += sizeof(int8_t) * data->lengths[CRAYON_TYPE_SINT8];

    crayon_misc_encode_to_buffer((uint8_t*)data->chars, buffer, sizeof(char) * data->lengths[CRAYON_TYPE_CHAR]);
    // buffer += sizeof(char) * data->lengths[CRAYON_TYPE_CHAR];

    return;
}

// This might be able to be optimised
int8_t
crayon_savefile_check_savedata(crayon_savefile_details_t* details, int8_t save_device_id) {
    // 0 for invalid savefile just incase we get an error
    details->savefile_versions[save_device_id] = 0;

    char* savename = crayon_savefile_get_device_path(details, save_device_id);
    if (!savename) {
        return -1;
    }

    FILE* fp = fopen(savename, "rb");
    free(savename);

    // File DNE
    if (!fp) {
        return -1;
    }

    crayon_savefile_version_t sf_version;

#if defined(_arch_dreamcast)

    fseek(fp, 0, SEEK_END);
    int pkg_size = ftell(fp);
    fseek(fp, 0, SEEK_SET);

    // Surely instead of doing the below, I can just read the header + version
    // But then again, I wouldn't be able to do the CRC check
    ;

    uint8_t* pkg_out = malloc(pkg_size);
    fread(pkg_out, pkg_size, 1, fp);
    fclose(fp);

    vmu_pkg_t pkg;
    if (vmu_pkg_parse(pkg_out, &pkg)) { // CRC is incorrect
        free(pkg_out);
        return -1;
    }

    free(pkg_out);

    // If the version IDs don't match, then this isn't the right savefile
    if (strcmp(pkg.app_id, details->strings[CRAYON_SF_STRING_APP_ID])) {
        return -1;
    }

    // Check to confirm the savefile version is not newer than it should be
    crayon_misc_encode_to_buffer((uint8_t*)&sf_version, (uint8_t*)pkg.data, sizeof(crayon_savefile_version_t));

#elif defined(_arch_pc)

    crayon_savefile_hdr_t hdr;

    fread(&hdr, 1, sizeof(hdr), fp);

    fread(&sf_version, 4, 1, fp);
    fclose(fp);

    // Pass that sf_version and maybe "hdr.app_id" through our endian-ness function
    crayon_misc_endian_correction((uint8_t*)&hdr, sizeof(hdr));
    crayon_misc_endian_correction((uint8_t*)&sf_version, sizeof(crayon_savefile_version_t));

    // Either it has the wrong size or the app ids somehow don't match
    // (The later should never trigger if you use this library right)
    if (strcmp(hdr.app_id, details->strings[CRAYON_SF_STRING_APP_ID])) {
        return -1;
    }

#else

#error "UNSUPPORTED ARCH"

#endif

    // Its valid, so set the right version number
    details->savefile_versions[save_device_id] = sf_version;

    return 0;
}

void
crayon_savefile_update_all_device_infos(crayon_savefile_details_t* details) {
    uint8_t i;
    for (i = 0; i < CRAYON_SF_NUM_SAVE_DEVICES; i++) {
        crayon_savefile_update_device_info(details, i);
    }

    return;
}

int8_t
crayon_savefile_update_device_info(crayon_savefile_details_t* details, int8_t save_device_id) {
    if (save_device_id < 0 || save_device_id >= CRAYON_SF_NUM_SAVE_DEVICES) {
        return -1;
    }

    // We start by assuming it fails all conditions
    details->present_devices &= ~(1 << save_device_id);
    details->present_savefiles &= ~(1 << save_device_id);
    details->upgradable_to_current &= ~(1 << save_device_id);

    FILE* fp;

#if defined(_arch_dreamcast)

    // Check if device is a memory card
    if (!crayon_peripheral_has_function(MAPLE_FUNC_MEMCARD, save_device_id)) {
        return -1;
    }

#endif

    // Any device thats present gets set
    crayon_savefile_set_device_bit(&details->present_devices, save_device_id);

    // Returns 0 if fine
    // If check threw an error, either it failed to construct the path string, open the file
    // Or there was some issue with the savefile (Failed CRC or wrong APP_ID)
    // So if the savefile does exist, then we say the device isn't present, else we check for space and see
    if (crayon_savefile_check_savedata(details, save_device_id)) {
        char* savename = crayon_savefile_get_device_path(details, save_device_id);
        if (!savename) {
            return -1;
        }

        // Calculate the size of the savefile to make sure if we have enough space
        fp = fopen(savename, "rb");
        free(savename);
        if (!fp) { // This usually means the file doesn't exist
            // Can't use details->savedata.size, doesn't include hdr and stuff
            if (crayon_savefile_get_savefile_size(details) <= crayon_savefile_devices_free_space(save_device_id)) {
                crayon_savefile_set_device_bit(&details->upgradable_to_current, save_device_id);
            }
        } else { // File does exist, so it must be invalid if check failed
            // We set present savefiles, even for invalid saves
            crayon_savefile_set_device_bit(&details->present_savefiles, save_device_id);
            fclose(fp);
        }
    } else {
        // Its a system requirement that the first sf version is 1 since 0 is used to mark invalid
        if (details->savefile_versions[save_device_id] == 0) {
            return -1;
        }

        // Even if we can't use it, we still show it
        crayon_savefile_set_device_bit(&details->present_savefiles, save_device_id);

        if (details->savefile_versions[save_device_id] == details->latest_version) {
            crayon_savefile_set_device_bit(&details->upgradable_to_current, save_device_id);
        } else if (details->savefile_versions[save_device_id] != 0
                   && details->savefile_versions[save_device_id] < details->latest_version) {
            char* savename = crayon_savefile_get_device_path(details, save_device_id);
            if (!savename) {
                return -1;
            }

            fp = fopen(savename, "rb");
            free(savename);
            if (!fp) { // Shouldn't occur
                return -1;
            }

            fseek(fp, 0, SEEK_END);
            uint32_t savefile_size = ftell(fp);
            fclose(fp);

#if defined(_arch_dreamcast)

            // Not a multiple of 512, fix it (Since we count in blocks and a block is 512 bytes)
            if (savefile_size % (1 << 9) != 0) {
                savefile_size &= ~((1 << 9) - 1); // Rounds down to nearest multiple of 512
                savefile_size += 512;             // Round up
            }

#endif

            if (crayon_savefile_get_savefile_size(details)
                <= crayon_savefile_devices_free_space(save_device_id) + savefile_size) {
                crayon_savefile_set_device_bit(&details->upgradable_to_current, save_device_id);
            }
        }
        // Future and "zero" version savefiles are always not upgradable.
    }

    return 0;
}

void
crayon_savefile_free_icon(crayon_savefile_details_t* details) {
#ifdef _arch_dreamcast

    if (details->icon_data) {
        free(details->icon_data);
    }
    if (details->icon_palette) {
        free(details->icon_palette);
    }
    details->icon_anim_count = 0;
    details->icon_anim_speed = 0;
    details->icon_data = NULL;
    details->icon_palette = NULL;

#endif

    return;
}

void
crayon_savefile_free_eyecatcher(crayon_savefile_details_t* details) {
#ifdef _arch_dreamcast

    if (details->eyecatcher_data) {
        free(details->eyecatcher_data);
    }
    details->eyecatcher_type = 0;
    details->eyecatcher_data = NULL;

#endif

    return;
}

void
crayon_savefile_free_savedata(crayon_savefile_data_t* savedata) {
    if (savedata->doubles) {
        free(savedata->doubles);
    }
    if (savedata->floats) {
        free(savedata->floats);
    }
    if (savedata->u32) {
        free(savedata->u32);
    }
    if (savedata->s32) {
        free(savedata->s32);
    }
    if (savedata->u16) {
        free(savedata->u16);
    }
    if (savedata->s16) {
        free(savedata->s16);
    }
    if (savedata->u8) {
        free(savedata->u8);
    }
    if (savedata->s8) {
        free(savedata->s8);
    }
    if (savedata->chars) {
        free(savedata->chars);
    }

    savedata->doubles = NULL;
    savedata->floats = NULL;
    savedata->u32 = NULL;
    savedata->s32 = NULL;
    savedata->u16 = NULL;
    savedata->s16 = NULL;
    savedata->u8 = NULL;
    savedata->s8 = NULL;
    savedata->chars = NULL;

    uint8_t i;
    for (i = 0; i < CRAYON_NUM_TYPES; i++) {
        savedata->lengths[i] = 0;
    }

    savedata->size = 0;

    return;
}
