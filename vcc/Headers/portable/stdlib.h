/* Minimal stand-in for the MSVC CRT header of the same name, used when VCC
   preprocesses with a non-MSVC preprocessor (Linux, browser playground). */
#ifndef _VCC_PORTABLE_STDLIB_H
#define _VCC_PORTABLE_STDLIB_H
#include <crtdefs.h>
void *malloc(size_t size);
void free(void *p);
void abort(void);
void exit(int status);
int abs(int x);
#endif
