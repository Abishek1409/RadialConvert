"""Tiny CLI around pdf2docx (MIT/AGPL-compatible free library) for RadialConvert.

Usage: pdf2docx.exe <input.pdf> <output.docx>
Exit codes: 0 ok, 1 conversion error, 2 bad arguments.
"""
import logging
import sys
import warnings


def main(argv: list[str]) -> int:
    if len(argv) != 3:
        print("usage: pdf2docx <input.pdf> <output.docx>", file=sys.stderr)
        return 2

    src, dst = argv[1], argv[2]
    # pdf2docx logs every page at INFO; keep stderr for real problems only.
    logging.disable(logging.INFO)
    warnings.simplefilter("ignore", FutureWarning)

    try:
        from pdf2docx import Converter

        cv = Converter(src)
        try:
            cv.convert(dst)
        finally:
            cv.close()
    except Exception as exc:  # report a clean one-line message to RadialConvert
        print(f"pdf2docx: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
