#!/usr/bin/env python3
"""Offline syntax/type check of Lanternvale's shaders: every CGPROGRAM block of every .shader under
Assets/Lanternvale/Resources/Shaders is compiled (vertex and fragment entry points, gamma and linear) with
glslangValidator's HLSL front end against a minimal UnityCG.cginc stand-in. Catches typos, undeclared names, type
mismatches and bad semantics; it cannot check Unity-specific ShaderLab state (Blend, Cull...) or real GPU behaviour."""
import os, re, subprocess, sys, tempfile, shutil

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SHADERS = os.path.join(ROOT, "Assets", "Lanternvale", "Resources", "Shaders")
STUB = os.path.dirname(os.path.abspath(__file__))

def main():
    exe = shutil.which("glslangValidator")
    if not exe:
        print("glslangValidator not found (apt-get install glslang-tools): shader check skipped")
        return 0
    failures = 0
    passes = 0
    for name in sorted(os.listdir(SHADERS)):
        if not name.endswith(".shader"):
            continue
        src = open(os.path.join(SHADERS, name), encoding="utf-8").read()
        blocks = re.findall(r"CGPROGRAM(.*?)ENDCG", src, re.S)
        for bi, block in enumerate(blocks):
            vert = re.search(r"#pragma\s+vertex\s+(\w+)", block)
            frag = re.search(r"#pragma\s+fragment\s+(\w+)", block)
            code = re.sub(r"#pragma[^\n]*", "", block)
            # Unity's VFACE: a float/fixed parameter; glslang wants SV_IsFrontFace on a bool
            code = re.sub(r"\b(fixed|half|float)\s+(\w+)\s*:\s*VFACE", r"bool \2 : SV_IsFrontFace", code)
            for gamma in (False, True):
                with tempfile.TemporaryDirectory() as tmp:
                    path = os.path.join(tmp, "pass.hlsl")
                    with open(path, "w", encoding="utf-8") as f:
                        if gamma:
                            f.write("#define UNITY_COLORSPACE_GAMMA 1\n")
                        f.write(code)
                    for stage, entry in (("vert", vert), ("frag", frag)):
                        if not entry:
                            continue
                        cmd = [exe, "-D", "-S", stage, "-e", entry.group(1), "-V", "--auto-map-bindings", "--auto-map-locations",
                               "-I" + STUB, "-I" + SHADERS, "-o", os.path.join(tmp, "out.spv"), path]
                        r = subprocess.run(cmd, capture_output=True, text=True)
                        passes += 1
                        if r.returncode != 0:
                            failures += 1
                            msg = "\n".join(l for l in (r.stdout + r.stderr).splitlines() if "ERROR" in l or "error" in l)
                            print(f"{name} pass {bi} {stage} ({'gamma' if gamma else 'linear'}): FAILED\n{msg}\n")
    print(f"shaders: {passes - failures}/{passes} stage compiles OK")
    return 1 if failures else 0

if __name__ == "__main__":
    sys.exit(main())
