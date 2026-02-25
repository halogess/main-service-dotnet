-- Migration: Add bab_skor to bab table
ALTER TABLE `bab`
ADD COLUMN `bab_skor` int(11) DEFAULT NULL AFTER `bab_pdf_path`;
