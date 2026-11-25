/* Minimal stand-in for the MSVC CRT header of the same name, used when VCC
   preprocesses with a non-MSVC preprocessor (Linux, browser playground). */
#ifndef _VCC_PORTABLE_STDINT_H
#define _VCC_PORTABLE_STDINT_H
#include <crtdefs.h>
typedef signed char int8_t;
typedef short int16_t;
typedef int int32_t;
typedef __int64 int64_t;
typedef unsigned char uint8_t;
typedef unsigned short uint16_t;
typedef unsigned int uint32_t;
typedef unsigned __int64 uint64_t;
#endif
