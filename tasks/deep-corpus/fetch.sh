#!/usr/bin/env bash
# Fixed upstream snapshots -> ignored local corpus. No source is executed.
set -euo pipefail
here=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo=$(cd -- "$here/../.." && pwd)
root="$repo/.cache/public-cobol-corpus/deep-selected"
archives="${COBOLCS_CORPUS_ARCHIVES:-$repo/.cache/public-cobol-corpus/.downloads}"
for command in curl unzip gzip tar sha256sum; do command -v "$command" >/dev/null || { echo "Missing: $command" >&2; exit 1; }; done
mkdir -p "$root" "$archives"
# A fresh private directory prevents extraction through existing symlinks.
stage=$(mktemp -d "$root/.fetch.XXXXXXXX")
trap 'rm -rf -- "$stage"' EXIT
if [[ -e "$root/READY.json" ]]; then echo "Existing completed corpus: $root (remove it explicitly to fetch again)"; exit 0; fi
gzip -dc "$here/files.tsv.gz" > "$stage/files.tsv"
while IFS=$'\t' read -r id url strip archive_hash archive_name; do
  archive="$archives/$archive_name"
  if [[ ! -f "$archive" ]]; then
    echo "Downloading $archive_name"
    curl --fail --location --retry 3 --connect-timeout 30 --max-time 900 --output "$archive.part" "$url"
    mv -- "$archive.part" "$archive"
  fi
  if [[ "$archive_hash" != '-' ]]; then
    actual=$(sha256sum "$archive"); actual=${actual%% *}
    [[ "$actual" == "$archive_hash" ]] || { echo "Archive hash mismatch: $archive_name" >&2; exit 1; }
  fi
  unzip -Z1 "$archive" > "$stage/entries"
  prefix=''
  if [[ "$strip" == 1 ]]; then IFS= read -r first < "$stage/entries"; prefix="${first%%/*}/"; fi
  count=0
  while IFS=$'\t' read -r origin entry destination expected; do
    [[ "$origin" == "$id" ]] || continue
    case "/$destination/" in *'/../'*|*'/./'*|*'//'*) echo 'Unsafe manifest path' >&2; exit 1;; esac
    [[ "$destination" != /* && "$destination" != *\\* ]] || exit 1
    target="$stage/data/$destination"
    mkdir -p -- "$(dirname -- "$target")"
    pattern="$prefix$entry"
    pattern=${pattern//\\/\\\\}; pattern=${pattern//\[/\\[}; pattern=${pattern//\*/\\*}; pattern=${pattern//\?/\\?}
    unzip -p "$archive" "$pattern" > "$target"
    actual=$(sha256sum "$target"); actual=${actual%% *}
    [[ "$actual" == "$expected" ]] || { echo "Source hash mismatch: $destination" >&2; exit 1; }
    count=$((count+1))
  done < "$stage/files.tsv"
  echo "Verified $archive_name: $count files"
done < "$here/origins.tsv"
tar -xzf "$here/metadata.tar.gz" -C "$stage/data"
# Do not merge into an existing or partially user-edited corpus.
for name in cobolcs-deep-samples COBOL_Files compact-selection; do
  [[ ! -e "$root/$name" && ! -L "$root/$name" ]] || { echo "Destination exists: $root/$name" >&2; exit 1; }
done
for name in cobolcs-deep-samples COBOL_Files compact-selection; do mv -- "$stage/data/$name" "$root/$name"; done
printf '{"sourceFiles":6578,"byteUniqueFiles":6514,"sha256Verified":true}\n' > "$root/READY.json"
echo "Ready: $root"
echo 'Next: follow tasks/deep-corpus/INSTRUCTIONS.md. Acquisition is not a conversion test.'
