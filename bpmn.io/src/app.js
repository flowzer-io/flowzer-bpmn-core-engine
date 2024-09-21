import 'bpmn-js/dist/assets/diagram-js.css';
import 'bpmn-js/dist/assets/bpmn-js.css';
import BpmnViewer from 'bpmn-js/lib/NavigatedViewer';

import 'bpmn-js/dist/assets/bpmn-font/css/bpmn-embedded.css';
import '@bpmn-io/properties-panel/assets/properties-panel.css';

import $ from 'jquery';
import BpmnModeler from 'bpmn-js/lib/Modeler';

import { debounce } from 'min-dash';


import {
  BpmnPropertiesPanelModule,
  BpmnPropertiesProviderModule,
  ZeebePropertiesProviderModule
} from 'bpmn-js-properties-panel';

import PropertiesProviderModul from './provider/flowzer';

// Camunda 8 moddle extension
import zeebeModdle from 'zeebe-bpmn-moddle/resources/zeebe';

// Camunda 8 behaviors
import ZeebeBehaviorsModule from 'camunda-bpmn-js-behaviors/lib/camunda-cloud';


console.log('Init');

window.InitEdit = async function(){
  var canvas = $('#js-canvas');

  // document.getElementById('js-canvas').style.backgroundColor = 'red';
  var bpmnModeler = new BpmnModeler({
    container: canvas,
    propertiesPanel: {
      parent: '#js-properties-panel'
    },
    additionalModules: [
      BpmnPropertiesPanelModule,
      BpmnPropertiesProviderModule,
      ZeebePropertiesProviderModule,
      ZeebeBehaviorsModule
    ],
    moddleExtensions: {
      zeebe: zeebeModdle
    }
  });

  window.bpmnModeler = bpmnModeler;
};


window.InitViewer = async function(){
  var canvas = $('#js-canvas');
  console.log('InitViewer');
  var bpmnModeler = new BpmnViewer({
    container: canvas
  });

  window.bpmnViewer = bpmnModeler;
};


console.log('Init completed');

window.InitCompleted = true;