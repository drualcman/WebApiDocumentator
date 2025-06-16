namespace WebApiDocumentator.Helpers;
internal static class Style
{
    public static string GetStyles()
    {
        return @"
    <style>
        :root {
            --primary-color: #4361ee;
            --primary-dark: #3a56d4;
            --secondary-color: #3f37c9;
            --success-color: #4cc9f0;
            --danger-color: #f72585;
            --warning-color: #f8961e;
            --info-color: #4895ef;
            --light-color: #f8f9fa;
            --dark-color: #212529;
            --gray-color: #6c757d;
            --light-gray: #e9ecef;
            --border-color: #dee2e6;
            --sidebar-width: 25%;
            --sidebar-bg: #2b2d42;
            --sidebar-text: #f8f9fa;
            --sidebar-hover: #3a3e5a;
            --sidebar-active: #ffffff;
            --content-bg: #ffffff;
            --example-bg: #f8f9fa;
            --code-bg: #1e1e1e;
            --code-text: #d4d4d4;
            --transition: all 0.3s ease;
        }

        * {
            box-sizing: border-box;
            margin: 0;
            padding: 0;
        }

        body {
            font-family: 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
            line-height: 1.6;
            color: #333;
            background-color: var(--content-bg);
            display: flex;
            min-height: 100vh;
            margin: 0;
        }

        .sidebar-toggle {
            position: fixed;
            right: 1rem;
            top: 1rem;
            z-index: 900;
            background: var(--primary-color);
            color: white;
            border: none;
            border-radius: 50%;
            width: 40px;
            height: 40px;
            font-size: 1.2rem;
            cursor: pointer;
            box-shadow: 0 2px 5px rgba(0, 0, 0, 0.2);
            transform: rotate(90deg);
        }  

            .sidebar-toggle.active {
                transform: unset;
            }

        #sidebar {
            width: var(--sidebar-width);
            background-color: var(--sidebar-bg);
            color: var(--sidebar-text);
            height: 100vh;
            position: fixed;
            right: -100%;
            top: 0;
            overflow-y: auto;
            transition: var(--transition);
            z-index: 10;
            box-shadow: 2px 0 10px rgba(0, 0, 0, 0.1);  .
            scroll-behavior: smooth;
        }

            #sidebar.active {
                right: 0;
            }

        .sidebar-header {
            padding: 1.5rem;
            border-bottom: 1px solid rgba(255, 255, 255, 0.1);
        }

            .sidebar-header h3 {
                color: white;
                margin: 0;
                font-size: 1.2rem;
            }

        .sidebar-search {
            padding: 0.5rem 1rem;
            position: relative;
        }

            .sidebar-search input {
                width: 100%;
                padding: 0.5rem 1rem 0.5rem 2rem;
                border-radius: 4px;
                border: none;
                background-color: rgba(255, 255, 255, 0.1);
                color: white;
            }

                .sidebar-search input::placeholder {
                    color: rgba(255, 255, 255, 0.5);
                }

        .sidebar-title {
            padding: 1rem 1.5rem;
            font-size: 0.9rem;
            text-transform: uppercase;
            letter-spacing: 1px;
            color: rgba(255, 255, 255, 0.7);
            display: flex;
            justify-content: space-between;
            align-items: center;
        }

            .sidebar-title .toggle-all {
                cursor: pointer;
                font-size: 0.8rem;
                color: var(--sidebar-text);
            }

        .endpoint-list {
            list-style: none;
            padding: 0;
            margin: 0;
        }         

            .endpoint-list .endpoint-list {
                margin-left: 1.5rem;
                border-left: 1px solid rgba(255, 255, 255, 0.1);
                padding-left: 0.5rem;
            }

        .endpoint-group {
            border-bottom: 1px solid rgba(255, 255, 255, 0.1);
            padding-bottom: 0.5rem;
        }

        .group-header {
            padding: 0.15rem 1rem;
            display: flex;
            justify-content: space-between;
            align-items: center;
            cursor: pointer;
            transition: var(--transition);
            user-select: none;
        }

            .group-header:hover {
                background-color: var(--sidebar-hover);
            }

        .group-title {
            font-weight: 500;
            display: flex;
            align-items: center; 
            flex-grow: 1;
        }

            .group-title::before {
                content: ""📁"";
                margin-right: 0.5rem;
                font-size: 0.9em;
            }

        .group-toggle {
            transition: transform 0.2s ease; 
            margin-left: 0.5rem;
            flex-shrink: 0; 
        }

            .group-toggle.collapsed {
                transform: rotate(-90deg);
            }

        .group-items {
            list-style: none;
            padding: 0;
            margin: 0;
            max-height: 0;
            overflow: hidden;
            transition: max-height 0.3s ease;
        }

        .endpoint-item {                            
            padding: 0.15rem 0.5rem 0.15rem 1rem;  .
            overflow: hidden; 
        }

        a.endpoint-link {
            text-decoration: none;
            color: rgba(255, 255, 255, 0.8);
            display: flex;
            align-items: center;
            font-size: 0.9rem;
            transition: var(--transition);
            overflow: hidden; 
            padding: 0.25rem 0.5rem;
            border-radius: 4px; 
            margin: 0.1rem 0; 
            word-break: break-word;
            white-space: normal; 
        }

            a.endpoint-link:hover {
                color: white;
                background-color: rgba(255, 255, 255, 0.1);
            }

            a.endpoint-link.selected {   
                background-color: var(--sidebar-active);
                color: black;
                font-weight: 600;
                padding: 0.5rem;
                overflow: visible;
            }

        .endpoint-method {
            display: inline-block;
            padding: 0.2rem 0.5rem;
            border-radius: 3px;
            font-size: 0.7rem;
            font-weight: 600;
            margin-right: 0.5rem;
            text-transform: uppercase;
            min-width: 50px;
            text-align: center;  
            flex-shrink: 0;
        }

        .endpoint-route {
            flex-grow: 1;
            overflow: hidden;
            word-break: break-word;
        }

        .GET {
            background-color: var(--success-color);
            color: black;
        }

        .POST {
            background-color: var(--info-color);
            color: white;
        }

        .PUT {
            background-color: var(--warning-color);
            color: black;
        }

        .DELETE {
            background-color: var(--danger-color);
            color: white;
        }

        .PATCH {
            background-color: #9d4edd;
            color: white;
        }

        .HEAD {
            background-color: #7b2cbf;
            color: white;
        }

        #content {
            padding: 1rem;
            flex: 1; 
        }

 
        #models-example,        
        {
            padding: 2rem;
            background-color: var(--example-bg);
            border-left: 1px solid var(--border-color);
        } 

        #examples {  
            min-width: 30%; 
            max-width: 35%; 
            padding: 1rem;
            background-color: var(--example-bg);
            border-left: 1px solid var(--border-color);
            position: sticky;
            overflow: auto;
            top: 0;
            height: 100vh;
        }

        .card, 
        .section {
            background-color: white;
            margin-bottom: .75rem;
        }  

        .card {
            border-radius: 8px;
            box-shadow: 0 2px 10px rgba(0, 0, 0, 0.05);
        }

        .section {
            padding: .2rem 1.5rem;
        }

        h1, h2, h3, h4, h5 {
            color: var(--dark-color);
            margin-bottom: 1rem;
        }

        h1 {
            font-size: 2rem;
        }

        h2 {
            font-size: 1.75rem;
        }

        h3 {
            font-size: 1.5rem;
        }

        h4 {
            font-size: 1.25rem;
        }

        h5 {
            font-size: 1rem;
        }

        p {
            margin-bottom: 1rem;
        }

       
        .endpoint-header {
            display: flex;
            align-items: center;
            gap: 1rem;
            margin-bottom: 1.5rem;
        }

        .endpoint-title {
            font-size: 1.75rem;
            margin: 0;
        }

        .endpoint-description {
            color: var(--gray-color);
            margin-bottom: .5rem;
        }

      
        .schema-table {
            width: 100%;
            border-collapse: collapse;
            margin: 1.5rem 0;
            background: white;
            border-radius: 8px;
            overflow: hidden;
            box-shadow: 0 1px 3px rgba(0, 0, 0, 0.1);
        }

            .schema-table th, .schema-table td {
                padding: 0.75rem 1rem;
                text-align: left;
                border-bottom: 1px solid var(--border-color);
            }

            .schema-table th {
                background-color: var(--light-gray);
                font-weight: 600;
                color: var(--dark-color);
            }

            .schema-table tr:last-child td {
                border-bottom: none;
            }

        .source {
            color: #7f8c8d;
            font-weight: 600;
        }

        .source.path {
            color: #4a6baf;
        }

        .source.query {
            color: #d35400;
        }

        .source.form {
            color: #27ae60;
        }

    
        .json-viewer {
            background-color: var(--code-bg);
            color: var(--code-text);
            padding: 1rem;
            border-radius: 8px;
            font-family: ""Consolas"", ""Courier New"", monospace;
            font-size: 0.9rem;
            white-space: pre-wrap;
            word-wrap: break-word;
            overflow-y: auto;
            max-height: 400px;
            margin: 1rem 0;
        }

      
        .test-form {
            margin-top: 2rem;
        }

        .form-group {
            margin-top: 1.5rem;
        }

        label {
            display: block;
            margin-bottom: 0.5rem;
            font-weight: 500;
        }

        input[type=""text""],
        input[type=""checkbox""],
        input[type=""datetime-local""],
        input[type=""password""],
        textarea,
        select {
            width: 100%;
            padding: 0.75rem;
            border: 1px solid var(--border-color);
            border-radius: 6px;
            font-family: inherit;
            font-size: 0.9rem;
            transition: border-color 0.2s;
        }

            input[type=""text""]:focus,
            input[type=""password""]:focus,
            textarea:focus,
            select:focus {
                outline: none;
                border-color: var(--primary-color);
                box-shadow: 0 0 0 3px rgba(67, 97, 238, 0.1);
            }

        textarea {
            min-height: 150px;
            resize: vertical;
        }

        .btn {
            display: inline-block;
            padding: 0.75rem 1.5rem;
            background-color: var(--primary-color);
            color: white;
            border: none;
            border-radius: 6px;
            font-size: 0.9rem;
            font-weight: 500;
            cursor: pointer;
            transition: var(--transition);
        }

            .btn:hover {
                background-color: var(--primary-dark);
            }

        .btn-secondary {
            background-color: var(--gray-color);
        }

            .btn-secondary:hover {
                background-color: #5a6268;
            }

        /* Auth section */
        .auth-section {
            margin-bottom: 1.5rem;
            padding: 1rem;
            background-color: var(--light-gray);
            border-radius: 8px;
        }

        .auth-fields {
            display: none;
            margin-top: 1rem;
            padding-top: 1rem;
            border-top: 1px solid var(--border-color);
        }

            .auth-fields.active {
                display: block;
            }

   
        .error-message {
            color: var(--danger-color);
            background-color: #f8d7da;
            padding: 0.75rem;
            border-radius: 6px;
            margin-bottom: 1rem;
            font-size: 0.9rem;
        }

        .field-error {
            color: var(--danger-color);
            font-size: 0.8rem;
            margin-top: 0.25rem;
        }
                
        .models-tabs,        
        .example-tabs {
            display: flex;
            border-bottom: 1px solid var(--border-color);
            margin-bottom: 1rem;
        }

        
        .models-tab,
        .example-tab {
            padding: 0.5rem 1rem;
            cursor: pointer;
            border-bottom: 2px solid transparent;
            transition: var(--transition);
        }
            .models-tab.active,
            .example-tab.active {
                border-bottom-color: var(--primary-color);
                color: var(--primary-color);
                font-weight: 500;
            }
        .models-tab-content,
        .example-tab-content {
            display: none;
        }
            .models-tab-content.active ,
            .example-tab-content.active {
                display: block;
            }


        @media (max-width: 1200px) {
            #examples {
                display: none;
            }
        }

        @media (max-width: 768px) {
            #content {
                min-width: 85%;
                margin-left: 0;
                padding: 1rem;
            }
        }   
    
        a.endpoint-link {
            font-size: 0.85rem;
        }

        .loading {
            display: none;
            text-align: center;
            padding: 1rem;
        }

        .loading-spinner {
            border: 3px solid rgba(0, 0, 0, 0.1);
            border-radius: 50%;
            border-top: 3px solid var(--primary-color);
            width: 20px;
            height: 20px;
            animation: spin 1s linear infinite;
            display: inline-block;
        }

        @keyframes spin {
            0% {
                transform: rotate(0deg);
            }

            100% {
                transform: rotate(360deg);
            }
        }
        .example-header {
            display: flex;
            justify-content: flex-end;
            margin-bottom: 0.5rem;
        }

        .copy-btn {
            background-color: var(--primary-color);
            color: white;
            border: none;
            border-radius: 4px;
            padding: 0.25rem 0.5rem;
            font-size: 0.8rem;
            cursor: pointer;
            transition: var(--transition);
        }

        .copy-btn:hover {
            background-color: var(--primary-dark);
        }

        .copy-btn.copied {
            background-color: var(--success-color);
        }
        .doc-tabs {
            display: flex;
            border-bottom: 1px solid var(--border-color);
            margin-bottom: 1.5rem;
        }

        .doc-tab {
            padding: 0.75rem 1.5rem;
            cursor: pointer;
            border-bottom: 3px solid transparent;
            font-weight: 500;
            transition: var(--transition);
        }

        .doc-tab:hover {
            color: var(--primary-color);
        }

        .doc-tab.active {
            border-bottom-color: var(--primary-color);
            color: var(--primary-color);
        }

        .doc-tab-content {
            display: none;
        }

        .doc-tab-content.active {
            display: block;
        }

        .collection-parameter {
            margin-bottom: 1rem;
            padding: 0.5rem;
            border: 1px solid #ddd;
            border-radius: 4px;
        }

        .collection-items {
            margin-top: 0.5rem;
        }

        .collection-item {
            display: flex;
            margin-bottom: 0.5rem;
        }

        .collection-item input {
            flex-grow: 1;
            margin-right: 0.5rem;
        }

        .btn-add-item, .btn-remove-item {
            background: #007bff;
            color: white;
            border: none;
            border-radius: 4px;
            padding: 0.25rem 0.5rem;
            cursor: pointer;
        }

        .btn-remove-item {
            background: #dc3545;
            padding: 0 0.5rem;
        }

        .btn-add-item:hover, .btn-remove-item:hover {
            opacity: 0.8;
        }

/* Header and Auth Section Layout - Fixed */
.header-section {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 1.5rem;
    margin-bottom: 1.5rem;
    align-items: start;
}

.headers-container {
    background: white;
    padding: 1rem;
    border-radius: 8px;
    box-shadow: 0 2px 10px rgba(0, 0, 0, 0.05);
    display: flex;
    flex-direction: column;
    gap: 0.75rem;
    overflow: visible; /* Asegura que nada se corte */
}

/* Header Rows - Fixed */
/* Header Rows with Validation */
.header-row {
    display: grid;
    grid-template-columns: 1fr 1fr auto;
    gap: 0.5rem;
    align-items: start; /* Cambiado a start para alinear arriba */
}

.header-input-group {
    display: contents; /* Los hijos participan directamente en el grid */
}

.header-name-container,
.header-value-container {
    display: flex;
    flex-direction: column;
    gap: 0.25rem;
}

.header-name, 
.header-value {
    width: 100%;
    padding: 0.5rem;
    border: 1px solid var(--border-color);
    border-radius: 4px;
    font-size: 0.9rem;
}

.field-error {
    color: var(--danger-color);
    font-size: 0.75rem;
    line-height: 1.2;
    margin-top: -0.25rem;
    padding: 0 0.25rem;
}

/* Botón remove ajustado */
.btn-icon.remove-header {
    margin-top: 0.5rem; /* Alineado con los inputs */
    width: 32px;
    height: 32px;
    padding: 0;
    display: flex;
    align-items: center;
    justify-content: center;
    background: transparent;
    border: none;
    color: var(--danger-color);
    cursor: pointer;
    flex-shrink: 0;
}

.btn-icon.remove-header:hover {
    background-color: rgba(247, 37, 133, 0.1);
    border-radius: 4px;
}

/* Auth Section - Fixed */
.auth-section {
    padding: 1rem;
    background: white;
    border-radius: 8px;
    box-shadow: 0 2px 10px rgba(0, 0, 0, 0.05);
    display: flex;
    flex-direction: column;
    gap: 0.75rem;
}

/* Responsive Design */
@media (max-width: 992px) {
    .header-section {
        grid-template-columns: 1fr;
    }
}

@media (max-width: 576px) {
    .header-row {
        grid-template-columns: 1fr 1fr auto;
    }
    
    .header-name,
    .header-value {
        padding: 0.5rem;
    }
}

/* Add Header Button */
#add-header {
    align-self: flex-start;
    display: flex;
    align-items: center;
    gap: 0.5rem;
    padding: 0.5rem 1rem;
}

#add-header svg {
    width: 16px;
    height: 16px;
}
   </style>
";
    }
}
