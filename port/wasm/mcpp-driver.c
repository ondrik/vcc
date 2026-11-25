/* Minimal command-line driver around the mcpp library: preprocessed text to stdout, diagnostics to stderr. */
#include <stdio.h>
#include "mcpp_lib.h"
int main(int argc, char **argv) {
  mcpp_use_mem_buffers(1);
  int rc = mcpp_lib_main(argc, argv);
  char *out = mcpp_get_mem_buffer(OUT), *err = mcpp_get_mem_buffer(ERR), *dbg = mcpp_get_mem_buffer(DBG);
  if (out) fputs(out, stdout);
  if (err) fputs(err, stderr);
  if (dbg) fputs(dbg, stderr);
  return rc;
}
