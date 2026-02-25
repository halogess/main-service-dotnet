-- Migration: Add bab_jumlah_kesalahan to bab table
ALTER TABLE `bab`
ADD COLUMN `bab_jumlah_kesalahan` int(10) unsigned DEFAULT NULL AFTER `bab_skor`;
