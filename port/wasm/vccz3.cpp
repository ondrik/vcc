// Thin interface over Z3 for the VCC playground: SMT-LIB text in, Z3's output text out.
// Mirrors ISolverBackend in port/ProverBridge.
//
// Each session is a cmd_context set up exactly like the z3 executable's SMT-LIB 2 front end
// (shell/smtlib_frontend.cpp). Z3_eval_smtlib2_string is not used: its command context shares
// the API context's ast_manager and, with Boogie's settings, can be dramatically slower (a VC
// the executable proves in a second ran for minutes).
#include <sstream>
#include <string>
#include <cstring>
#include <emscripten.h>
#include "util/gparams.h"
#include "util/memory_manager.h"
#include "util/env_params.h"
#include "util/z3_exception.h"
#include "cmd_context/cmd_context.h"
#include "parsers/smt2/smt2parser.h"
#include "muz/fp/dl_cmds.h"
#include "cmd_context/extra_cmds/proof_cmds.h"
#include "opt/opt_cmds.h"
#include "smt/smt2_extra_cmds.h"
#include "solver/solver.h"

static std::string result;

static std::string error_sexpr(char const* msg) {
  std::string s = "(error \"";
  for (char const* p = msg; *p; ++p) s += (*p == '"') ? '\'' : *p;
  return s + "\")\n";
}

// global set-up done by the executable's main() before anything else
static void ensure_initialized() {
  static bool initialized = false;
  if (!initialized) {
    memory::initialize(0);
    initialized = true;
  }
}

extern "C" {

EMSCRIPTEN_KEEPALIVE void vccz3_global_param_set(const char* name, const char* value) {
  ensure_initialized();
  try { gparams::set(name, value); } catch (z3_exception&) { /* unknown parameter: ignore */ }
}

EMSCRIPTEN_KEEPALIVE cmd_context* vccz3_open(void) {
  ensure_initialized();
  env_params::updt_params();   // applies memory_max_size etc. set through vccz3_global_param_set
  auto* ctx = alloc(cmd_context);
  ctx->set_solver_factory(mk_smt_strategic_solver_factory());
  install_dl_cmds(*ctx);
  install_opt_cmds(*ctx);
  install_smt2_extra_cmds(*ctx);
  install_proof_cmds(*ctx);
  return ctx;
}

// Returns what the z3 executable would print for these commands; valid until the next call.
EMSCRIPTEN_KEEPALIVE const char* vccz3_eval(cmd_context* ctx, const char* commands) {
  std::istringstream in(commands);
  std::ostringstream out;
  ctx->set_regular_stream(out);
  ctx->set_diagnostic_stream(out);
  try {
    parse_smt2_commands(*ctx, in);
  } catch (z3_exception& e) {
    out << error_sexpr(e.what());
  } catch (std::exception& e) {
    out << error_sexpr(e.what());
  }
  ctx->set_regular_stream("stdout");
  ctx->set_diagnostic_stream("stderr");
  result = out.str();
  return result.c_str();
}

EMSCRIPTEN_KEEPALIVE void vccz3_close(cmd_context* ctx) {
  dealloc(ctx);
}

}
