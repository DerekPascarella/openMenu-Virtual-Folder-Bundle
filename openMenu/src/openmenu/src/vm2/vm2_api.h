#pragma once

// ReSharper disable once CppUnusedIncludeDirective
#include <stdio.h>

#include <dc/maple.h>

typedef struct maple_alldevinfo {
    uint32 functions;          /**< \brief Function codes supported */
    uint32 function_data[3];   /**< \brief Additional data per function */
    uint8 area_code;           /**< \brief Region code */
    uint8 connector_direction; /**< \brief ? */
    char product_name[30];     /**< \brief Name of device */
    char product_license[60];  /**< \brief License statement */
    uint16 standby_power;      /**< \brief Power consumption (standby) */
    uint16 max_power;          /**< \brief Power consumption (max) */
    char extended[40];
    char free[40];
} maple_alldevinfo_t;

int vm2_set_id(maple_device_t* dev, const char* ID, const char* name);
int check_vm2_present(maple_device_t* dev);
const char* get_last_allinfo_type_name(void);
const char* get_vmu_type_name(maple_device_t* dev);
