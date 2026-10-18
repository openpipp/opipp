# CLI contract as emitted by the PIPP GUI (ground truth)

Extracted from the decompiled `PIPP.exe` (`generate_cmdline_options_*`). Each row is one
`arg_list.Add(...)` call, with the nearest enclosing guard. The engine adds further switches
itself (`-temp_dir`, `-dark_tmp`, `-dark_pgm`, `-flat_tmp`, `-flat_dark_tmp`, `-gui`,
`-test_run`, `-sdp`) which are listed at the end.


## generate_cmdline_options_animation_options_tab

| Guard | Emission |
|---|---|
| `if (animation_forwardCB.Enabled && animation_forwardCB.Checked)` | `arg_list.Add("-anim_fwd");` |
| `if (repeat_forward_lastCB.Checked)` | `arg_list.Add("-anim_fwd_lf_rep=" + repeat_forward_lastNUD.Value);` |
| `if (animation_reverseCB.Enabled && animation_reverseCB.Checked)` | `arg_list.Add("-anim_rev");` |
| `if (repeat_reverse_lastCB.Checked)` | `arg_list.Add("-anim_rev_lf_rep=" + repeat_reverse_lastNUD.Value);` |
| `if (animation_repeat_allCB.Enabled && animation_repeat_allCB.Checked)` | `arg_list.Add("-anim_all_rep=" + animation_repeat_allNUD.Value);` |
| `if (animation_reverse_not_in_last_repeatCB.Checked)` | `arg_list.Add("-no_anim_rev_last_rep");` |

## generate_cmdline_options_source_flat_dark_files_tab

| Guard | Emission |
|---|---|
| `if (flat_dark_frame_subtractionCB.Checked)` | `arg_list.Add("-flat_dark");` |
| `if (save_master_flat_dark_CB.Checked)` | `arg_list.Add("-save_flat_dark");` |

## generate_cmdline_options_source_flat_files_tab

| Guard | Emission |
|---|---|
| `if (flat_frame_calibrationCB.Checked)` | `arg_list.Add("-flat");` |
| `if (save_master_flat_CB.Checked)` | `arg_list.Add("-save_flat");` |
| `if (flat_data_has_bayerCB.Checked)` | `arg_list.Add("-flat_bayer");` |

## generate_cmdline_options_source_dark_files_tab

| Guard | Emission |
|---|---|
| `if (dark_frame_subtractionCB.Checked)` | `arg_list.Add("-dark");` |
| `if (save_master_dark_CB.Checked)` | `arg_list.Add("-save_dark");` |
| `if (replace_hot_pixelsCB.Checked)` | `arg_list.Add("-hot_pixel=" + hot_pixel_thresholdNUD.Value);` |
| `if (dark_data_has_bayerCB.Checked)` | `arg_list.Add("-dark_bayer");` |

## generate_cmdline_options_input_options_tab

| Guard | Emission |
|---|---|
| `if (raw_hpfCB.Checked)` | `arg_list.Add("-raw_hpf");` |
| `if (raw_image_debayerCB.Checked)` | `arg_list.Add("-raw_db=" + raw_debayer_algorithmCB.SelectedIndex);` |
| `if (raw_image_debayerCB.Checked)` | `arg_list.Add("-raw_ocs=" + raw_output_colour_spaceCB.SelectedIndex);` |
| `if (raw_image_debayerCB.Checked)` | `arg_list.Add("-raw_hl=" + raw_highlight_recovery_typeCB.SelectedIndex);` |
| `if (y800decodestrictCB.Checked)` | `arg_list.Add("-y8");` |
| `if (datetimeTB.Text != "")` | `arg_list.Add("-wjpos_dtmask=" + datetimeTB.Text);` |
| `if (date_time_extract_conv_utCB.Checked)` | `arg_list.Add("-wjpos_lt2ut");` |
| `if (date_time_extract_conv_utCB.Checked)` | `arg_list.Add("-wjpos_ipfps=" + inputframespersecondNUD.Value);` |
| `case 1:` | `arg_list.Add("-ser_bpp=16");` |
| `case 2:` | `arg_list.Add("-ser_bpp=15");` |
| `case 3:` | `arg_list.Add("-ser_bpp=14");` |
| `case 4:` | `arg_list.Add("-ser_bpp=13");` |
| `case 5:` | `arg_list.Add("-ser_bpp=12");` |
| `case 6:` | `arg_list.Add("-ser_bpp=11");` |
| `case 7:` | `arg_list.Add("-ser_bpp=10");` |
| `case 8:` | `arg_list.Add("-ser_bpp=9");` |
| `case 9:` | `arg_list.Add("-ser_bpp=8");` |
| `if (startframeNUD.Value > 1m)` | `arg_list.Add("-sf=" + startframeNUD.Value);` |
| `if (endframeNUD.Value > 0m)` | `arg_list.Add("-f=" + (endframeNUD.Value - startframeNUD.Value + 1m));` |
| `if (endframeNUD.Value > 0m)` | `arg_list.Add("-ctrf");` |
| `if (endframeNUD.Value > 0m)` | `arg_list.Add("-f=" + numframesNUD.Value);` |
| `if (ipframedropNUD.Value > 0m)` | `arg_list.Add("-ifd=" + ipframedropNUD.Value);` |
| `case 1:` | `arg_list.Add("-mono");` |
| `case 2:` | `arg_list.Add("-colour");` |
| `if (debayerenableGB.Enabled && debayerframesCB.Checked)` | `arg_list.Add("-db");` |
| `if (protectbayerCB.Enabled && protectbayerCB.Checked)` | `arg_list.Add("-pb");` |
| `if (debayerGB.Enabled)` | `arg_list.Add("-db_type=" + debay_typeCB.SelectedIndex);` |
| `if (debayerGB.Enabled)` | `arg_list.Add("-db=" + debay_patternCB.SelectedIndex);` |
| `if (binningCB.SelectedIndex > 0)` | `arg_list.Add("-bin=" + (binningCB.SelectedIndex + 1));` |
| `if (binning_calculationCB.SelectedIndex == 1)` | `arg_list.Add("-bin_sum");` |

## generate_cmdline_options_menu_strip

| Guard | Emission |
|---|---|
| `if (saveRejectedFramesToolStripMenuItem.Checked)` | `arg_list.Add("-sr");` |
| `if (!use_ram_buffersMenuItem.Checked)` | `arg_list.Add("-no_ram_buff");` |

## generate_cmdline_options_output_options_tab

| Guard | Emission |
|---|---|
| `if (subdir_append_date_timeCB.Checked)` | `arg_list.Add("-osubdir=" + text);` |
| `if (test_run)` | `arg_list.Add("-od=" + output_directory);` |
| `if (test_run)` | `arg_list.Add("-od=" + text2);` |
| `if (test_run)` | `arg_list.Add("-od=" + text3);` |
| `if (outputprefixTB.Text != "")` | `arg_list.Add("-op=" + outputprefixTB.Text);` |
| `if (outputsuffixTB.Text != "")` | `arg_list.Add("-os=" + outputsuffixTB.Text);` |
| `if (test_run)` | `arg_list.Add("-bmp");` |
| `if (outputaviRB.Checked)` | `arg_list.Add("-avi");` |
| `if (outputserRB.Checked)` | `arg_list.Add("-ser");` |
| `if (outputbmpRB.Checked)` | `arg_list.Add("-bmp");` |
| `if (outputtifRB.Checked)` | `arg_list.Add("-tif");` |
| `if (outputpngRB.Checked)` | `arg_list.Add("-png");` |
| `if (outputfitsRB.Checked)` | `arg_list.Add("-fits");` |
| `if (outputgifRB.Checked)` | `arg_list.Add("-gif");` |
| `if (avi_outputGB.Enabled)` | `arg_list.Add("-avi_codec=" + avi_codecCB.SelectedIndex);` |
| `if (avi_outputGB.Enabled && codec_qualityGB.Enabled)` | `arg_list.Add("-avi_quality=" + codec_qualityNUD.Value);` |
| `if (old_format_max_sizeCB.SelectedIndex == 0)` | `arg_list.Add("-avi_old4");` |
| `if (old_format_max_sizeCB.SelectedIndex == 0)` | `arg_list.Add("-avi_old");` |
| `if (avi_safe_frame_widthCB.Enabled && avi_safe_frame_widthCB.Checked)` | `arg_list.Add("-avi_safe_width");` |
| `if (avi_fpsCB.Checked)` | `arg_list.Add("-auto_fps");` |
| `if (num % 10 != 0)` | `arg_list.Add("-fps_rate=" + num);` |
| `if (num % 10 != 0)` | `arg_list.Add("-fps_scale=" + num2);` |
| `if (gif_repeat_foreverCB.Checked)` | `arg_list.Add("-anim_all_rep_always");` |
| `if (gif_fpsCB.Checked)` | `arg_list.Add("-auto_fps");` |
| `if (num3 % 10 != 0)` | `arg_list.Add("-fps_rate=" + num3);` |
| `if (num3 % 10 != 0)` | `arg_list.Add("-fps_scale=" + num4);` |
| `if (num3 % 10 != 0)` | `arg_list.Add("-gif_quant_type=" + gif_quantCB.SelectedIndex);` |
| `if (num3 % 10 != 0)` | `arg_list.Add("-gif_border_tol=" + num5);` |
| `if (num3 % 10 != 0)` | `arg_list.Add("-gif_trns_pxl_tol=" + num6);` |
| `if (num3 % 10 != 0)` | `arg_list.Add("-gif_col_tbl_depth=" + num7);` |
| `if (num3 % 10 != 0)` | `arg_list.Add("-gif_lossy_comp=" + num8);` |
| `if (renumberGB.Enabled && keepimagefilenamesCB.Checked)` | `arg_list.Add("-keepimgname");` |
| `if (renumberGB.Enabled && noframerenumberCB.Checked)` | `arg_list.Add("-nr");` |
| `if (renumberGB.Enabled && !addleadingzerosCB.Checked)` | `arg_list.Add("-nlz");` |
| `if (renumberGB.Enabled && includequalityvalueCB.Checked)` | `arg_list.Add("-incqv");` |
| `if (rgbsplitGB.Enabled && rgbsplitCB.Checked && !test_run)` | `arg_list.Add("-rgb");` |
| `if (outputframedropNUD.Value > 0m)` | `arg_list.Add("-drop=" + outputframedropNUD.Value);` |
| `if (winjuposfilenameCB.Checked)` | `arg_list.Add("-wjpos_time=" + winjupostimeCB.SelectedIndex);` |

## generate_cmdline_options_processing_options_tab

| Guard | Emission |
|---|---|
| `if (input_gainNUD.Value != 1m && input_gainNUD.Value > 0m)` | `arg_list.Add("-input_gain=" + input_gainNUD.Value.ToString(CultureInfo.CreateSpecificCulture("en-GB")));` |
| `if (gammaNUD.Value != 1m && gammaNUD.Value > 0m)` | `arg_list.Add("-gamma=" + gammaNUD.Value.ToString(CultureInfo.CreateSpecificCulture("en-GB")));` |
| `if (median_filterCB.Checked)` | `arg_list.Add("-median");` |
| `case 0:` | `arg_list.Add("-convmono=3");` |
| `case 1:` | `arg_list.Add("-convmono=2");` |
| `case 2:` | `arg_list.Add("-convmono=1");` |
| `case 3:` | `arg_list.Add("-convmono=0");` |
| `case 4:` | `arg_list.Add("-convmono=4");` |
| `case 5:` | `arg_list.Add("-convmono=5");` |
| `case 6:` | `arg_list.Add("-convmono=6");` |
| `case 6:` | `arg_list.Add("-convmono=3");` |
| `if (hs_white_pointCB.Checked)` | `arg_list.Add("-hs=" + hs_white_pointNUD.Value);` |
| `if (hs_black_pointCB.Checked)` | `arg_list.Add("-hs_bp");` |
| `if ((hs_white_pointCB.Checked \|\| hs_black_pointCB.Checked) && rgbhistoCB.Checked)` | `arg_list.Add("-hsrgb");` |
| `if (overexposedCB.Checked)` | `arg_list.Add("-oe=" + overexposedNUD.Value);` |
| `if (flip_xCB.Checked)` | `arg_list.Add("-flip_x");` |
| `if (flip_yCB.Checked)` | `arg_list.Add("-flip_y");` |
| `case 1:` | `arg_list.Add("-rotate90");` |
| `case 2:` | `arg_list.Add("-rotate270");` |
| `case 3:` | `arg_list.Add("-rotate180");` |
| `if (stabilise_objectRB.Checked && checkplanetCB.Checked)` | `arg_list.Add("-chk");` |
| `if (stabilise_objectRB.Checked && checkplanetCB.Checked)` | `arg_list.Add("-ms=" + minsizeNUD.Value);` |
| `if (minpixelvalueGB.Enabled && !autothresholdCB.Checked)` | `arg_list.Add("-mp=" + thresholdNUD.Value);` |
| `if (centreplanetCB.Enabled && centreplanetCB.Checked)` | `arg_list.Add("-ctr");` |
| `if (edge_in_shadowCB.SelectedIndex > 0)` | `arg_list.Add("-shdw=" + edge_in_shadowCB.SelectedIndex);` |
| `if (enablecroppingPANEL.Enabled)` | `arg_list.Add("-cx=" + cropwidthNUD.Value);` |
| `if (enablecroppingPANEL.Enabled)` | `arg_list.Add("-cy=" + cropheightNUD.Value);` |
| `if (xoffsetNUD.Value != 0m)` | `arg_list.Add("-ox=" + xoffsetNUD.Value);` |
| `if (yoffsetNUD.Value != 0m)` | `arg_list.Add("-oy=" + yoffsetNUD.Value);` |
| `if (stabilise_surfaceRB.Checked && enableSurfaceTrackingCB.Checked)` | `arg_list.Add("-en_surface_feature_tracking");` |
| `if (enablestabilisationCB.Enabled && enablestabilisationCB.Checked)` | `arg_list.Add("-en_ss");` |
| `if (enablestabilisationCB.Enabled && enablestabilisationCB.Checked)` | `arg_list.Add("-afb_x=" + afb_xNUD.Value);` |
| `if (enablestabilisationCB.Enabled && enablestabilisationCB.Checked)` | `arg_list.Add("-afb_y=" + afb_yNUD.Value);` |
| `if (enablestabilisationCB.Enabled && enablestabilisationCB.Checked)` | `arg_list.Add("-afb_width=" + afb_widthNUD.Value);` |
| `if (enablestabilisationCB.Enabled && enablestabilisationCB.Checked)` | `arg_list.Add("-afb_height=" + afb_heightNUD.Value);` |
| `if (afb_rejectCB.Checked)` | `arg_list.Add("-freject");` |
| `if (aoi_enableCB.Checked)` | `arg_list.Add("-aoi");` |
| `if (aoi_enableCB.Checked)` | `arg_list.Add("-aoi_x=" + aoi_xNUD.Value);` |
| `if (aoi_enableCB.Checked)` | `arg_list.Add("-aoi_y=" + aoi_yNUD.Value);` |
| `if (aoi_enableCB.Checked)` | `arg_list.Add("-aoi_width=" + aoi_widthNUD.Value);` |
| `if (aoi_enableCB.Checked)` | `arg_list.Add("-aoi_height=" + aoi_heightNUD.Value);` |
| `if (aoi_rejectCB.Checked)` | `arg_list.Add("-aoi_reject");` |
| `if (aoi_cropCB.Checked)` | `arg_list.Add("-aoi_crop");` |
| `if (resize_framesCB.Checked)` | `arg_list.Add("-resize_en");` |
| `if (resize_framesCB.Checked)` | `arg_list.Add("-resize_width=" + resize_widthNUD.Value);` |
| `if (resize_keep_aspect_ratioCB.Checked)` | `arg_list.Add("-resize_height=-1");` |
| `if (resize_keep_aspect_ratioCB.Checked)` | `arg_list.Add("-resize_height=" + resize_heightNUD.Value);` |

## generate_cmdline_options_quality_options_tab

| Guard | Emission |
|---|---|
| `if (quality_limiting_typeCB.SelectedIndex == 0)` | `arg_list.Add("-q=" + qualityframesNUD.Value);` |
| `if (quality_limiting_typeCB.SelectedIndex == 0)` | `arg_list.Add("-qpcnt=" + qualityframesNUD.Value);` |
| `if (quality_limiting_typeCB.SelectedIndex == 0)` | `arg_list.Add("-q");` |
| `if (!qreorderCB.Checked)` | `arg_list.Add("-nr");` |
| `if (ninoxalgorithmCB.Checked)` | `arg_list.Add("-qold");` |
| `if (histoalgorithmCB.Checked)` | `arg_list.Add("-qhisto");` |
| `if (totalpixelalgorithmCB.Checked)` | `arg_list.Add("-qtotal");` |
| `if (qualitysortingCB.Checked && !test_run && qualityoptionsGB.Enabled)` | `arg_list.Add("-qmin=" + mindownsampleNUD.Value);` |
| `if (qualitysortingCB.Checked && !test_run && qualityoptionsGB.Enabled)` | `arg_list.Add("-qmax=" + maxdownsampleNUD.Value);` |
| `if (qualitysortingCB.Checked && !test_run && qualityoptionsGB.Enabled)` | `arg_list.Add("-qinc=" + downsampleincNUD.Value);` |
| `if (qualitysortingCB.Checked && !test_run && qualityweightGB.Enabled && qualityweightingCB.Checked)` | `arg_list.Add("-qw");` |

## do_processing_thread

| Guard | Emission |
|---|---|
| `if (test_run)` | `arg_list2.Add("-temp_dir=" + temp_directory);` |
| `if (num >= 0)` | `arg_list2.Add("-dark_tmp=" + master_dark_tmp_file);` |
| `if (num >= 0)` | `arg_list2.Add("-dark_pgm=" + master_dark_pgm_file);` |
| `if (num2 >= 0)` | `arg_list2.Add("-flat_dark_tmp=" + master_flat_dark_tmp_file);` |
| `if (num3 >= 0)` | `arg_list2.Add("-flat_tmp=" + master_flat_tmp_file);` |

## generate_cmdline_options

| Guard | Emission |
|---|---|
| `if (dialogResult != DialogResult.Yes)` | `arg_list.Add("-gui");` |
| `if (test_run)` | `arg_list.Add("-test_run");` |
| `if (show_detected_pixels)` | `arg_list.Add("-sdp");` |

## generate_cmdline_options_source_files_tab

| Guard | Emission |
|---|---|
| `if (image_filelist.Items.Count > 1 && joinmodeRB.Checked)` | `arg_list.Add("-join");` |
