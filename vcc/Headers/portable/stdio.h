/* Minimal stand-in for the MSVC CRT header of the same name, used when VCC
   preprocesses with a non-MSVC preprocessor (Linux, browser playground). */
#ifndef _VCC_PORTABLE_STDIO_H
#define _VCC_PORTABLE_STDIO_H
#include <crtdefs.h>
int printf(const char *format, ...);
#endif
