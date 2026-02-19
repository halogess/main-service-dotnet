-- Migration: Add dokumen_report_path to dokumen table

ALTER TABLE `dokumen`
ADD COLUMN `dokumen_report_path` VARCHAR(255) NULL AFTER `dokumen_pdf_path`;

-- Verify
-- DESCRIBE `dokumen`;
