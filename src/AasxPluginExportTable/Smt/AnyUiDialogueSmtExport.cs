﻿/*
Copyright (c) 2018-2022 Festo AG & Co. KG <https://www.festo.com/net/de_de/Forms/web/contact_international>
Author: Michael Hoffmeister

This source code is licensed under the Apache License 2.0 (see LICENSE.txt).

This source code may use other Open Source software components (see LICENSE.txt).
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Xml;
using System.Xml.Schema;
using AasxIntegrationBase;
using AasxIntegrationBase.AasForms;
using Newtonsoft.Json;
using Aas = AasCore.Aas3_0;
using AdminShellNS;
using Extensions;
using AnyUi;
using AasxPluginExportTable.Uml;

namespace AasxPluginExportTable.Smt
{
    /// <summary>
    /// This class allows exporting a Submodel to various UML formats.
    /// Note: it is a little misplaced in the "export table" plugin, however the
    /// domain is quite the same and maybe special file format dependencies will 
    /// be re equired in the future.
    /// </summary>
    public static class AnyUiDialogueSmtExport
    {
        public static async Task ExportSmtDialogBased(
            LogInstance log,
            AasxMenuActionTicket ticket,
            AnyUiContextPlusDialogs displayContext,
            ExportTableOptions pluginOptionsTable)
        {
            // access
            if (ticket == null || displayContext == null)
                return;

            // check preconditions
            if (ticket.Env == null || ticket.Submodel == null || ticket.SubmodelElement != null)
            {
                log?.Error("Export AsciiDoc SMT spec: A Submodel has to be selected!");
                return;
            }

            // ask for parameter record?
            var record = ticket["Record"] as ExportSmtRecord;
            if (record == null)
                record = new ExportSmtRecord();

            // arguments by reflection
            ticket?.ArgValue?.PopulateObjectFromArgs(record);

            // maybe given a format name?
            if (ticket["Format"] is string fmt)
                for (int i = 0; i < ExportSmtRecord.FormatNames.Length; i++)
                    if (ExportSmtRecord.FormatNames[i].ToLower()
                            .Contains(fmt.ToLower()))
                        record.Format = (ExportSmtRecord.ExportFormat)i;

            // ok, go on ..
            var uc = new AnyUiDialogueDataModalPanel("Export SMT spec as AsciiDoc ..");
            uc.ActivateRenderPanel(record,
                (uci) =>
                {
                    // create panel
                    var panel = new AnyUiStackPanel();
                    var helper = new AnyUiSmallWidgetToolkit();

                    var g = helper.AddSmallGrid(4, 2, new[] { "220:", "*" },
                                padding: new AnyUiThickness(0, 5, 0, 5));
                    panel.Add(g);

                    // Row 0 : Format
                    helper.AddSmallLabelTo(g, 0, 0, content: "Format output:",
                        verticalAlignment: AnyUiVerticalAlignment.Center,
                        verticalContentAlignment: AnyUiVerticalAlignment.Center);
                    AnyUiUIElement.SetIntFromControl(
                        helper.Set(
                            helper.AddSmallComboBoxTo(g, 0, 1,
                                items: ExportSmtRecord.FormatNames,
                                selectedIndex: (int)record.Format),
                                minWidth: 600, maxWidth: 600),
                        (i) => { record.Format = (ExportSmtRecord.ExportFormat)i; });

                    // Row 1 : Format tables
                    if (pluginOptionsTable?.Presets != null)
                    {
                        helper.AddSmallLabelTo(g, 1, 0, content: "From options:",
                            verticalAlignment: AnyUiVerticalAlignment.Center,
                            verticalContentAlignment: AnyUiVerticalAlignment.Center);

                        AnyUiComboBox cbPreset = null;
                        cbPreset = AnyUiUIElement.RegisterControl(
                            helper.Set(
                                helper.AddSmallComboBoxTo(g, 1, 2,
                                    items: pluginOptionsTable.Presets.Select((pr) => "" + pr.Name).ToArray(),
                                    text: "Please select preset to load .."),
                                minWidth: 350, maxWidth: 400),
                                (o) =>
                                {
                                    if (!cbPreset.SelectedIndex.HasValue)
                                        return new AnyUiLambdaActionNone();
                                    var ndx = cbPreset.SelectedIndex.Value;
                                    if (ndx < 0 || ndx >= pluginOptionsTable.Presets.Count)
                                        return new AnyUiLambdaActionNone();
                                    record.PresetTables = pluginOptionsTable.Presets[ndx].Name;
                                    return new AnyUiLambdaActionModalPanelReRender(uc);
                                });

                    }

                    // Row 2 : Export HTML
                    helper.AddSmallLabelTo(g, 2, 0, content: "Export HTML:",
                        verticalAlignment: AnyUiVerticalAlignment.Center,
                        verticalContentAlignment: AnyUiVerticalAlignment.Center);
                    AnyUiUIElement.SetBoolFromControl(
                        helper.Set(
                            helper.AddSmallCheckBoxTo(g, 2, 1,
                                content: "(export command given by options will be executed)",
                                isChecked: record.ExportHtml,
                                verticalContentAlignment: AnyUiVerticalAlignment.Center),
                                colSpan: 2),
                        (b) => { record.ExportHtml = b; });

                    // Row 3 : Export PDF
                    helper.AddSmallLabelTo(g, 3, 0, content: "Export PDF:",
                        verticalAlignment: AnyUiVerticalAlignment.Center,
                        verticalContentAlignment: AnyUiVerticalAlignment.Center);
                    AnyUiUIElement.SetBoolFromControl(
                        helper.Set(
                            helper.AddSmallCheckBoxTo(g, 3, 1,
                                content: "(export command given by options will be executed)",
                                isChecked: record.ExportPdf,
                                verticalContentAlignment: AnyUiVerticalAlignment.Center),
                                colSpan: 2),
                        (b) => { record.ExportPdf = b; });

                    // give back
                    return panel;
                });

            // scriptmode or ui?
            if (!(ticket?.ScriptMode == true && ticket["File"] != null))
            {
                if (!(await displayContext.StartFlyoverModalAsync(uc)))
                    return;

                // stop
                await Task.Delay(2000);
            }

            // ask for filename?
            if (!(await displayContext.MenuSelectSaveFilenameToTicketAsync(
                        ticket, "File",
                        "Select file for SMT specification to AsciiDoc ..",
                        "new.puml",
                        "AsciiDoc (*.adoc)|*.adoc|ZIP archive (*.zip)|*.zip|All files (*.*)|*.*",
                        "SMT specification to AsciiDoc: No valid filename.",
                        argLocation: "Location",
                        reworkSpecialFn: true)))
                return;

            var fn = ticket["File"] as string;
            var loc = ticket["Location"];

            // the Submodel elements need to have parents
            var sm = ticket.Submodel;
            sm.SetAllParents();

            // export
            ExportUml.ExportUmlToFile(ticket.Env, sm, record, fn);

            // persist
            await displayContext.CheckIfDownloadAndStart(log, loc, fn);           

            log.Info($"Export \"SMT specification to AsciiDoc file: {fn}");
        }
    }
}
