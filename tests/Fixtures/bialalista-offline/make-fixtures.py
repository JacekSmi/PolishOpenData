r"""Rebuilds the synthetic flat-file fixtures of the offline Biala Lista tests (stdlib only).

Run from anywhere:  python -I make-fixtures.py
Needs 7-Zip at C:\Program Files\7-Zip\7z.exe (override with the SEVENZIP environment variable).
"""
import hashlib
import json
import os
import subprocess
from pathlib import Path

HERE = Path(__file__).resolve().parent
SEVENZIP = os.environ.get("SEVENZIP", r"C:\Program Files\7-Zip\7z.exe")
ROUNDS = 3
DATE = "20261013"
NIP = "5261040828"
MASKS = [
    "XX10201026XXXXXXXXYYYYYYXX",
    "XX10201026XXXXXXXXXXYYYYXX",
    "XX12400001XXXXXXXXXXXXYYYY",
]


def flat_hash(date: str, nip: str, nrb: str) -> str:
    # SHA-512 applied ROUNDS times; every round after the first hashes the lowercase hex of the previous digest.
    value = date + nip + nrb
    for _ in range(ROUNDS):
        value = hashlib.sha512(value.encode("utf-8")).hexdigest()
    return value


def sha512_hex(text: str) -> str:
    return hashlib.sha512(text.encode("utf-8")).hexdigest()


def write_json(path: Path, header_date: str) -> bytes:
    active = [flat_hash(DATE, NIP, "06160011271843983820000034"),
              # virtual account 60102010260000042270201111 is found through the mask XX10201026XXXXXXXXXX2011XX
              flat_hash(DATE, NIP, "XX10201026XXXXXXXXXX2011XX")]
    exempt = [flat_hash(DATE, "7171642051", "61109010140000071219812874")]
    active += [sha512_hex(f"filler-active-{i}") for i in range(20)]
    exempt += [sha512_hex(f"filler-exempt-{i}") for i in range(20)]
    document = {
        "naglowek": {
            "dataGenerowaniaDanych": header_date,
            "liczbaTransformacji": str(ROUNDS),
            "schemat": "RRRRMMDDNNNNNNNNNNBBBBBBBBBBBBBBBBBBBBBBBBBB",
        },
        "skrotyPodatnikowCzynnych": active,  # unsorted on purpose
        "skrotyPodatnikowZwolnionych": exempt,
        "maski": MASKS,
    }
    data = json.dumps(document, indent=2).encode("utf-8")
    path.write_bytes(data)
    return data


def build_archive(date: str, sum_source: bytes) -> None:
    json_name = f"{date}.json"
    sum_name = f"{json_name}.sha512sum"
    (HERE / sum_name).write_text(f"{hashlib.sha512(sum_source).hexdigest()}  {json_name}\n", encoding="ascii", newline="\n")
    archive = HERE / f"{date}.7z" if date == DATE else HERE / f"{date}-badsum.7z"
    archive.unlink(missing_ok=True)
    subprocess.run([SEVENZIP, "a", "-t7z", "-m0=LZMA2", str(archive), json_name, sum_name], cwd=HERE, check=True)


def main() -> None:
    data = write_json(HERE / f"{DATE}.json", DATE)
    build_archive(DATE, data)
    # second archive: the sum belongs to different content, so verification must fail
    bad_json = HERE / "20261014.json"
    write_json(bad_json, "20261014")
    build_archive("20261014", b"not the content of the json")
    bad_json.unlink()
    (HERE / "20261014.json.sha512sum").unlink()


if __name__ == "__main__":
    main()
