# NotMyFault Azure IoT Edge module

A basic Azure IoT Edge module that publishes custom metrics to be collected by the Azure Monitor module.

Then, use the provided workbook to analyze such metrics and follow the guide to create alert rules.

## Features

This module has no production features but it is meant to test Azure Monitor integration with Azure IoT Edge module.

This module implements a direct method that terminates executable itself. Invoking this direct method multiple times is useful to test same scenarios on the cloud side.

To collect and transport metrics from this sample module, consult the [official docs](https://docs.microsoft.com/azure/iot-edge/how-to-add-custom-metrics?view=iotedge-2020-11#configure-the-metrics-collector-to-collect-custom-metrics).