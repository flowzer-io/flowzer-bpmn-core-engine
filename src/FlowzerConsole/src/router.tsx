import {
  createRootRoute,
  createRoute,
  createRouter,
  Outlet,
  useNavigate,
  useParams,
  useSearch,
} from '@tanstack/react-router';

import { WorkflowEditorSession } from '@/components/workflow-editor/WorkflowEditorSession';
import { AppShell } from '@/components/layout/AppShell';
import { Button } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/Card';
import { DashboardPage } from '@/pages/DashboardPage';
import { DecisionsPage } from '@/pages/DecisionsPage';
import { FormsPage } from '@/pages/FormsPage';
import { InstanceDetailPage } from '@/pages/InstanceDetailPage';
import { InstancesPage } from '@/pages/InstancesPage';
import { ModelerPage } from '@/pages/ModelerPage';
import { OperationsPage } from '@/pages/OperationsPage';
import { OutlinePage } from '@/pages/OutlinePage';
import { TasksPage } from '@/pages/TasksPage';
import { WorkflowsPage } from '@/pages/WorkflowsPage';
import { AiConnectionsPage } from '@/pages/AiConnectionsPage';

const rootRoute = createRootRoute({
  notFoundComponent: NotFound,
});

/** Alles Uebrige laeuft in der Anwendungshuelle, die die Anmeldung voraussetzt. */
const shellRoute = createRoute({
  getParentRoute: () => rootRoute,
  id: 'shell',
  component: AppShell,
});

const dashboardRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/',
  component: DashboardPage,
});

const workflowsRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/workflows',
  component: Outlet,
});

const workflowsIndexRoute = createRoute({
  getParentRoute: () => workflowsRoute,
  path: '/',
  component: WorkflowsPage,
});

const workflowEditorRoute = createRoute({
  getParentRoute: () => workflowsRoute,
  path: '$definitionId',
  component: WorkflowEditorRoute,
});

function WorkflowEditorRoute() {
  const { definitionId } = useParams({ from: workflowEditorRoute.id });
  const id = decodeURIComponent(definitionId);
  return <WorkflowEditorSession key={id} definitionId={id}><Outlet /></WorkflowEditorSession>;
}

const workflowDetailRoute = createRoute({
  getParentRoute: () => workflowEditorRoute,
  path: '/',
  validateSearch: (search: Record<string, unknown>): WorkflowDetailSearch => ({
    element: typeof search.element === 'string' ? search.element : undefined,
  }),
  component: WorkflowDetailRoute,
});

interface WorkflowDetailSearch {
  element?: string;
}

function WorkflowDetailRoute() {
  const { definitionId } = useParams({ from: workflowDetailRoute.id });
  const { element } = useSearch({ from: workflowDetailRoute.id });
  return <ModelerPage definitionId={decodeURIComponent(definitionId)} focusElementId={element} />;
}

/**
 * Die Gliederung ist eine eigene Adresse neben dem Diagramm, keine Umschaltung
 * darin: Beide Ansichten teilen einen ungespeicherten, routegebundenen Arbeitsstand.
 */
const workflowOutlineRoute = createRoute({
  getParentRoute: () => workflowEditorRoute,
  path: 'gliederung',
  component: WorkflowOutlineRoute,
});

function WorkflowOutlineRoute() {
  const { definitionId } = useParams({ from: workflowOutlineRoute.id });
  return <OutlinePage definitionId={decodeURIComponent(definitionId)} />;
}

const instancesRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/instances',
  component: Outlet,
});

const instancesIndexRoute = createRoute({
  getParentRoute: () => instancesRoute,
  path: '/',
  component: InstancesPage,
});

const instanceDetailRoute = createRoute({
  getParentRoute: () => instancesRoute,
  path: '$instanceId',
  component: InstanceDetailRoute,
});

function InstanceDetailRoute() {
  const { instanceId } = useParams({ from: instanceDetailRoute.id });
  return <InstanceDetailPage instanceId={instanceId} />;
}

const formsRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/forms',
  component: FormsPage,
});

const formSectionsRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/form-sections',
  // Bestehende Lesezeichen bleiben nutzbar, zeigen aber keine zweite Bibliothek mehr.
  component: FormsPage,
});

const decisionsRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/decisions',
  component: DecisionsPage,
});

const operationsRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/operations',
  component: OperationsPage,
});

const aiConnectionsRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/ai-connections',
  component: AiConnectionsPage,
});

interface TasksSearch {
  task?: string;
}

const tasksRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/tasks',
  validateSearch: (search: Record<string, unknown>): TasksSearch => ({
    task: typeof search.task === 'string' ? search.task : undefined,
  }),
  component: TasksRoute,
});

function TasksRoute() {
  const { task } = useSearch({ from: tasksRoute.id });
  const navigate = useNavigate();

  return (
    <TasksPage
      selectedTaskId={task}
      onSelectTask={(taskId) =>
        void navigate({ to: '/tasks', search: taskId ? { task: taskId } : {} })
      }
    />
  );
}

function NotFound() {
  const navigate = useNavigate();

  return (
    <div className="grid h-full place-items-center p-10">
      <EmptyState
        icon="explore_off"
        title="Seite nicht gefunden"
        description="Diese Adresse gibt es in der Flowzer Console nicht."
        action={
          <Button variant="primary" icon="home" onClick={() => void navigate({ to: '/' })}>
            Zum Dashboard
          </Button>
        }
      />
    </div>
  );
}

const routeTree = rootRoute.addChildren([
  shellRoute.addChildren([
    dashboardRoute,
    workflowsRoute.addChildren([workflowsIndexRoute, workflowEditorRoute.addChildren([workflowOutlineRoute, workflowDetailRoute])]),
    instancesRoute.addChildren([instancesIndexRoute, instanceDetailRoute]),
    formsRoute,
    formSectionsRoute,
    decisionsRoute,
    aiConnectionsRoute,
    operationsRoute,
    tasksRoute,
  ]),
]);

export const router = createRouter({
  routeTree,
  defaultPreload: 'intent',
  scrollRestoration: true,
});

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router;
  }
}
