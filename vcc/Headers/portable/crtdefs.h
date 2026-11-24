/* Minimal stand-in for the MSVC CRT header of the same name, used when VCC
   preprocesses with a non-MSVC preprocessor (Linux, browser playground). */
#ifndef _VCC_PORTABLE_CRTDEFS_H
#define _VCC_PORTABLE_CRTDEFS_H

#ifdef _WIN64
typedef unsigned __int64 size_t;
typedef __int64 ptrdiff_t;
typedef __int64 intptr_t;
typedef unsigned __int64 uintptr_t;
#else
typedef unsigned int size_t;
typedef int ptrdiff_t;
typedef int intptr_t;
typedef unsigned int uintptr_t;
#endif
typedef unsigned short wchar_t;

#ifndef NULL
#define NULL ((void*)0)
#endif

#define __cdecl
#define __stdcall
#define __fastcall

#endif
