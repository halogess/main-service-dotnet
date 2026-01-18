-- Migration: Update template table columns
-- Replace template_filepath with template_docx_path, template_pdf_path, and add template_created_at

-- Step 1: Add new columns
ALTER TABLE `template` 
ADD COLUMN `template_docx_path` VARCHAR(500) NULL AFTER `template_status`,
ADD COLUMN `template_pdf_path` VARCHAR(500) NULL AFTER `template_docx_path`,
ADD COLUMN `template_created_at` DATETIME DEFAULT CURRENT_TIMESTAMP AFTER `template_pdf_path`;

-- Step 2: Migrate existing data (copy filepath to docx_path)
UPDATE `template` 
SET `template_docx_path` = `template_filepath`
WHERE `template_filepath` IS NOT NULL;

-- Step 3: Set created_at for existing rows if null
UPDATE `template` 
SET `template_created_at` = NOW()
WHERE `template_created_at` IS NULL;

-- Step 4: Drop old column (run this after verifying data migration)
-- WARNING: Only run after confirming step 2 was successful
ALTER TABLE `template` 
DROP COLUMN `template_filepath`;

-- Verify the changes
-- DESCRIBE `template`;
