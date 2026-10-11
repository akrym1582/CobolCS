#!/usr/bin/env bash
# Fixed upstream snapshots -> ignored local corpus. No source is executed.
set -euo pipefail
here=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo=$(cd -- "$here/../.." && pwd)
root="$repo/.cache/public-cobol-corpus/deep-selected"
cached_only=false
if [[ "${1:-}" == '--cached-only' && $# == 1 ]]; then
  cached_only=true
  root="$repo/.cache/public-cobol-corpus/deep-available"
elif [[ $# != 0 ]]; then
  echo 'Usage: fetch.sh [--cached-only]' >&2; exit 2
fi
archives="${COBOLCS_CORPUS_ARCHIVES:-$repo/.cache/public-cobol-corpus/.downloads}"
for command in curl unzip zipinfo gzip tar sha256sum sort uniq awk; do command -v "$command" >/dev/null || { echo "Missing: $command" >&2; exit 1; }; done
mkdir -p "$root" "$archives"
# A fresh private directory prevents extraction through existing symlinks.
stage=$(mktemp -d "$root/.fetch.XXXXXXXX")
trap 'rm -rf -- "$stage"' EXIT
if [[ -e "$root/READY.json" ]]; then echo "Existing completed corpus: $root (remove it explicitly to fetch again)"; exit 0; fi
gzip -dc "$here/files.tsv.gz" > "$stage/files.tsv"
missing=0
verified=0
rejected=0
while IFS=$'\t' read -r id url strip archive_hash archive_name; do
  archive="$archives/$archive_name"
  if [[ ! -f "$archive" ]]; then
    if $cached_only; then
      echo "Not acquired: $archive_name"
      missing=$((missing+1))
      continue
    fi
    echo "Downloading $archive_name"
    curl --fail --location --retry 3 --connect-timeout 30 --max-time 900 --output "$archive.part" "$url"
    mv -- "$archive.part" "$archive"
  fi
  if [[ "$archive_hash" != '-' ]]; then
    actual=$(sha256sum "$archive"); actual=${actual%% *}
    [[ "$actual" == "$archive_hash" ]] || { echo "Archive hash mismatch: $archive_name" >&2; exit 1; }
  fi
  unzip -Z1 "$archive" > "$stage/entries"
  # Validate names before selecting entries; never interpret an archive link.
  while IFS= read -r entry; do
    case "/$entry" in *'/../'*|*'/./'*|*'//'*) echo 'Unsafe archive path' >&2; exit 1;; esac
    [[ "$entry" != /* && "$entry" != *\\* ]] || { echo 'Unsafe archive path' >&2; exit 1; }
  done < "$stage/entries"
  [[ -z "$(sort "$stage/entries" | uniq -d)" ]] || { echo 'Duplicate archive entry' >&2; exit 1; }
  zipinfo -l "$archive" > "$stage/attributes"
  if awk 'substr($1,1,1)=="l" { found=1 } END { exit !found }' "$stage/attributes"; then
    if $cached_only; then
      echo "Rejected archive with symlinks: $archive_name"
      missing=$((missing+1))
      rejected=$((rejected+1))
      continue
    fi
    echo 'Archive symlink is not accepted' >&2; exit 1
  fi
  # Bound declared decompression size without a limit below the selected X corpus.
  awk 'NF >= 9 && $4 ~ /^[0-9]+$/ { total += $4 } END { exit total > 8589934592 }' "$stage/attributes" || { echo 'Archive exceeds 8 GiB expanded limit' >&2; exit 1; }
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
  verified=$((verified+count))
done < "$here/origins.tsv"
mkdir -p "$stage/data"
tar -xzf "$here/metadata.tar.gz" -C "$stage/data"
# Do not merge into an existing or partially user-edited corpus.
for name in cobolcs-deep-samples COBOL_Files compact-selection; do
  [[ ! -e "$root/$name" && ! -L "$root/$name" ]] || { echo "Destination exists: $root/$name" >&2; exit 1; }
done
for name in cobolcs-deep-samples COBOL_Files compact-selection; do
  if [[ -d "$stage/data/$name" ]]; then mv -- "$stage/data/$name" "$root/$name"; fi
done
if [[ "$missing" != 0 ]]; then
  printf '{"complete":false,"missingArchives":%s,"rejectedArchives":%s,"verifiedFiles":%s,"expectedSourceFiles":6578}\n' "$missing" "$rejected" "$verified" > "$root/INCOMPLETE.json"
  echo "Partial verified input: $root; $missing archives unavailable. No READY.json created."
  exit 0
fi
printf '{"sourceFiles":6578,"byteUniqueFiles":6514,"sha256Verified":true}\n' > "$root/READY.json"
echo "Ready: $root"
echo 'Next: follow tasks/deep-corpus/INSTRUCTIONS.md. Acquisition is not a conversion test.'
