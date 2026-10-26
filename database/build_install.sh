#!/usr/bin/env bash
set -euo pipefail

database_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
install_path="${database_dir}/install_demo.sql"
temporary_path="$(mktemp "${database_dir}/.install_demo.sql.XXXXXX")"
trap 'rm -f -- "$temporary_path"' EXIT

cat > "$temporary_path" <<'SQL'
-- tệp sinh tự động, không sửa tay
:on error exit
USE [master];
GO
IF DB_ID(N'$(DbName)') IS NOT NULL
BEGIN
    ALTER DATABASE [$(DbName)] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [$(DbName)];
END;
GO
CREATE DATABASE [$(DbName)] COLLATE Vietnamese_CI_AS;
GO
ALTER DATABASE [$(DbName)] SET READ_COMMITTED_SNAPSHOT ON;
GO
USE [$(DbName)];
GO
SQL

for script_name in \
    001_schema.sql \
    002_indexes.sql \
    003_views.sql \
    004_security.sql \
    010_seed_reference.sql \
    020_seed_demo.sql
do
    script_path="${database_dir}/${script_name}"
    if [[ -f "$script_path" ]]; then
        printf '\n-- %s\n' "$script_name" >> "$temporary_path"
        cat -- "$script_path" >> "$temporary_path"
        printf '\n' >> "$temporary_path"
    fi
done

chmod 644 "$temporary_path"
mv -f -- "$temporary_path" "$install_path"
printf 'Đã sinh %s\n' "$install_path"
