/* Minimal stand-in for the MSVC CRT header of the same name, used when VCC
   preprocesses with a non-MSVC preprocessor (Linux, browser playground). */
#ifndef _VCC_PORTABLE_STDDEF_H
#define _VCC_PORTABLE_STDDEF_H
#include <crtdefs.h>
#define offsetof(s, m) ((size_t)&(((s *)0)->m))
#endif
