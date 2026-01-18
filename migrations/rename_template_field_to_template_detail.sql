-- Migration: Rename template_field to template_detail
-- Date: 2026-01-18
-- Description: Renames table and columns from template_field to template_detail,
--              renames template_field_key to template_detail_field,
--              and adds new column template_detail_catatan

-- Step 1: Rename the table
RENAME TABLE `template_field` TO `template_detail`;

-- Step 2: Rename the primary key column
ALTER TABLE `template_detail` 
  CHANGE COLUMN `template_field_id` `template_detail_id` int(10) unsigned NOT NULL AUTO_INCREMENT;

-- Step 3: Rename the text column
ALTER TABLE `template_detail` 
  CHANGE COLUMN `template_field_text` `template_detail_text` varchar(100) NOT NULL;

-- Step 4: Rename the key column to field (template_field_key -> template_detail_field)
ALTER TABLE `template_detail` 
  CHANGE COLUMN `template_field_key` `template_detail_field` varchar(100) DEFAULT NULL;

-- Step 5: Add the new catatan column
ALTER TABLE `template_detail` 
  ADD COLUMN `template_detail_catatan` varchar(100) DEFAULT NULL AFTER `template_detail_field`;
